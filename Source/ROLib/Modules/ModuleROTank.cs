﻿using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using KSPShaderTools;
using static ROLib.ROLLog;
using ROLib.Utils;

namespace ROLib
{

    // TODO: prevDiameter allows the surface attached parts to update, so I should make a prevHeight to allow the surface attached parts on the top and bottom to update as well.
    /// <summary>
    /// PartModule that manages multiple models/meshes and accompanying features for model switching - resources, modules, textures, recoloring.<para/>
    /// Includes 3 stack-mounted modules.  All modules support model-switching, texture-switching, recoloring.
    /// </summary>
    public class ModuleROTank : PartModule, IPartCostModifier, IPartMassModifier, IRecolorable, IContainerVolumeContributor
    {
        private const string GroupDisplayName = "RO-Tanks";
        private const string GroupName = "ModuleROTank";
        private const float minModelRatio = 0.5f;
        private const float maxModelRatio = 8;

        #region KSPFields

        [KSPField] public float diameterLargeStep = 0.1f;
        [KSPField] public float diameterSmallStep = 0.1f;
        [KSPField] public float diameterSlideStep = 0.001f;
        [KSPField] public float minDiameter = 0.1f;
        [KSPField] public float maxDiameter = 100.0f;
        [KSPField] public float minLength = 0.1f;
        [KSPField] public float maxLength = 100.0f;
        [KSPField] public float actualHeight = 0.0f;

        [KSPField] public float volumeScalingPower = 3f;
        [KSPField] public float massScalingPower = 3f;
        [KSPField] public bool enableVScale = true;
        [KSPField] public bool lengthWidth = false;
        [KSPField] public bool scaleMass = false;
        [KSPField] public bool scaleCost = false;
        
        [KSPField] public int coreContainerIndex = 0;
        [KSPField] public int noseContainerIndex = 0;
        [KSPField] public int mountContainerIndex = 0;
        [KSPField] public string coreManagedNodes = string.Empty;
        [KSPField] public string noseManagedNodes = string.Empty;
        [KSPField] public string mountManagedNodes = string.Empty;
        [KSPField] public string noseInterstageNode = "noseinterstage";
        [KSPField] public string mountInterstageNode = "mountinterstage";

        /// <summary>
        /// The current user selected diamater of the part.  Drives the scaling and positioning of everything else in the model.
        /// </summary>
        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Diameter", guiUnits = "m", groupName = GroupName, groupDisplayName = GroupDisplayName),
         UI_FloatEdit(sigFigs = 4, suppressEditorShipModified = true)]
        public float currentDiameter = 1.0f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Length", guiUnits = "m", groupName = GroupName),
         UI_FloatEdit(sigFigs = 4, suppressEditorShipModified = true)]
        public float currentLength = 1.0f;

        [KSPEvent(guiName = "Open Diameter Selection", guiActiveEditor = true, groupName = GroupName)]
        public void OpenTankDimensionGUIEvent() => EditDimensions();

        /// <summary>
        /// Adjustment to the vertical-scale of v-scale compatible models/module-slots.
        /// </summary>
        [KSPField(isPersistant = true, guiName = "V.ScaleAdj", groupName = GroupName),
         UI_FloatEdit(sigFigs = 4, suppressEditorShipModified = true, minValue = -1, maxValue = 1, incrementLarge = 0.25f, incrementSmall = 0.05f, incrementSlide = 0.001f)]
        public float currentVScale = 0f;

        /// <summary>
        /// This is the total length of the entire tank with the nose, core and mounts all considered.
        /// </summary>
        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Total Length", guiFormat = "F4", guiUnits = "m", groupName = GroupName)]
        public float totalTankLength = 0.0f;

        /// <summary>
        /// This is the largest diameter of the entire tank.
        /// </summary>
        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Largest Diameter", guiFormat = "F4", guiUnits = "m", groupName = GroupName)]
        public float largestDiameter = 0.0f;

        //------------------------------------------MODEL SELECTION SET PERSISTENCE-----------------------------------------------//

        //non-persistent value; initialized to whatever the currently selected core model definition is at time of loading
        //allows for variant names to be updated in the part-config without breaking everything....
        [KSPField(isPersistant = true, guiName = "Variant", guiActiveEditor = true, groupName = GroupName),
         UI_ChooseOption(suppressEditorShipModified = true)]
        public string currentVariant = "Default";

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Nose", groupName = GroupName),
         UI_ChooseOption(suppressEditorShipModified = true)]
        public string currentNose = "Mount-None";

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Core", groupName = GroupName),
         UI_ChooseOption(suppressEditorShipModified = true)]
        public string currentCore = "Mount-None";

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Mount", groupName = GroupName),
         UI_ChooseOption(suppressEditorShipModified = true)]
        public string currentMount = "Mount-None";

        //------------------------------------------TEXTURE SET PERSISTENCE-----------------------------------------------//

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Nose Tex", groupName = GroupName),
         UI_ChooseOption(suppressEditorShipModified = true)]
        public string currentNoseTexture = "default";

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Core Tex", groupName = GroupName),
         UI_ChooseOption(suppressEditorShipModified = true)]
        public string currentCoreTexture = "default";

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Mount Tex", groupName = GroupName),
         UI_ChooseOption(suppressEditorShipModified = true)]
        public string currentMountTexture = "default";

        //-------------------------------------RESETTING MODEL TO ORIGINAL SIZE------------------------------------------//
        [KSPEvent(guiActiveEditor = true, guiName = "Reset Model to Original", groupName = GroupName)]
        public void ResetModel()
        {
            if (lengthWidth) return;

            currentDiameter = coreModule.definition.diameter;
            currentVScale = 0;

            this.ROLupdateUIFloatEditControl(nameof(currentDiameter), minDiameter, maxDiameter, diameterLargeStep, diameterSmallStep, diameterSlideStep);
            this.ROLupdateUIFloatEditControl(nameof(currentVScale), -1, 1, 0.25f, 0.05f, 0.001f);
            ModelChangedHandlerWithSymmetry(true, true);
            MonoUtilities.RefreshPartContextWindow(part);
        }

        //------------------------------------------RECOLORING PERSISTENCE-----------------------------------------------//

        //persistent data for modules; stores colors
        [KSPField(isPersistant = true)]
        public string noseModulePersistentData = string.Empty;

        [KSPField(isPersistant = true)]
        public string coreModulePersistentData = string.Empty;

        [KSPField(isPersistant = true)]
        public string mountModulePersistentData = string.Empty;

        //tracks if default textures and resource volumes have been initialized; only occurs once during the parts' first Start() call
        [KSPField(isPersistant = true)]
        public bool initializedDefaults = false;

        #endregion KSPFields

        #region Private Variables

        /// <summary>
        /// Standard work-around for lack of config-node data being passed consistently and lack of support for mod-added serializable classes.
        /// </summary>
        [Persistent]
        public string configNodeData = string.Empty;

        /// <summary>
        /// Has initialization been run?  Set to true the first time init methods are run (OnLoad/OnStart), and ensures that init is only run a single time.
        /// </summary>
        private bool initialized = false;

        /// <summary>
        /// The adjusted modified mass for this part.
        /// </summary>
        private float modifiedMass = -1;

        /// <summary>
        /// The adjusted modified cost for this part.
        /// </summary>
        private float modifiedCost = -1;

        /// <summary>
        /// Previous diameter value, used for surface attach position updates.
        /// </summary>
        private float prevDiameter = -1;
        private float prevLength = -1;

        private string[] noseNodeNames;
        private string[] coreNodeNames;
        private string[] mountNodeNames;

        //Main module slots for nose/core/mount
        private ROLModelModule<ModuleROTank> noseModule;
        private ROLModelModule<ModuleROTank> coreModule;
        private ROLModelModule<ModuleROTank> mountModule;

        /// <summary>
        /// Mapping of all of the variant sets available for this part.  When variant list length > 0, an additional 'variant' UI slider is added to allow for switching between variants.
        /// </summary>
        private readonly Dictionary<string, ModelDefinitionVariantSet> variantSets = new Dictionary<string, ModelDefinitionVariantSet>();

        /// <summary>
        /// Helper method to get or create a variant set for the input variant name.  If no set currently exists, a new set is empty set is created and returned.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        private ModelDefinitionVariantSet GetVariantSet(string name)
        {
            if (!variantSets.TryGetValue(name, out ModelDefinitionVariantSet set))
            {
                set = new ModelDefinitionVariantSet(name);
                variantSets.Add(name, set);
            }
            return set;
        }

        /// <summary>
        /// Find the first variant set containing a definition with ModelDefinitionLayoutOptions def.  Will not create a new set if not found.
        /// </summary>
        /// <param name="def"></param>
        /// <returns></returns>
        private ModelDefinitionVariantSet GetVariantSet(ModelDefinitionLayoutOptions def) =>
            variantSets.Values.Where(a => a.definitions.Contains(def)).FirstOrDefault();

        ModelDefinitionLayoutOptions[] coreDefs;
        ModelDefinitionLayoutOptions[] noseDefs;
        ModelDefinitionLayoutOptions[] mountDefs;

        private DimensionWindow dimWindow;

        #endregion Private Variables

        internal void ModelChangedHandler(bool pushNodes)
        {
            ValidateLength();
            UpdateModulePositions();
            UpdateTankVolume(lengthWidth);
            UpdateDimensions();
            UpdateModelMeshes();
            UpdateAttachNodes(pushNodes);
            UpdateAvailableVariants();
            UpdateDragCubes();
            if (scaleMass)
                UpdateMass();
            if (scaleCost)
                UpdateCost();
            ROLStockInterop.UpdatePartHighlighting(part);
            if (HighLogic.LoadedSceneIsEditor)
                GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
        }

        internal void ModelChangedHandlerWithSymmetry(bool pushNodes, bool symmetry)
        {
            ModelChangedHandler(pushNodes);
            if (symmetry)
            {
                foreach (Part p in part.symmetryCounterparts)
                {
                    p.FindModuleImplementing<ModuleROTank>().ModelChangedHandler(pushNodes);
                }
            }
        }

        #region Standard KSP Overrides

        public override void OnLoad(ConfigNode node)
        {
            if (string.IsNullOrEmpty(configNodeData)) { configNodeData = node.ToString(); }
            Initialize();
        }

        public override void OnStart(StartState state)
        {
            Initialize();
            ModelChangedHandler(false);
            InitializeUI();
            if (HighLogic.LoadedSceneIsFlight && vessel is Vessel && vessel.rootPart == part)
                GameEvents.onFlightReady.Add(UpdateDragCubes);
            initializedDefaults = true;
        }

        public void OnDestroy()
        {
            if (HighLogic.LoadedSceneIsEditor)
                GameEvents.onEditorShipModified.Remove(OnEditorVesselModified);
            GameEvents.onFlightReady.Remove(UpdateDragCubes);
        }

        private void OnEditorVesselModified(ShipConstruct ship) => UpdateAvailableVariants();

        // IPartMass/CostModifier overrides
        public ModifierChangeWhen GetModuleMassChangeWhen() => ModifierChangeWhen.FIXED;
        public ModifierChangeWhen GetModuleCostChangeWhen() => ModifierChangeWhen.FIXED;
        public float GetModuleMass(float defaultMass, ModifierStagingSituation sit) => Mathf.Max(0, modifiedMass);
        public float GetModuleCost(float defaultCost, ModifierStagingSituation sit) => Mathf.Max(0, modifiedCost);

        #endregion Standard KSP Overrides

        #region IRecolorable and IContainerVolumeContributor Overrides

        public string[] getSectionNames() => new string[] { "Nose", "Core", "Mount" };

        public RecoloringData[] getSectionColors(string section)
        {
            return section switch
            {
                "Nose" => noseModule.recoloringData,
                "Core" => coreModule.recoloringData,
                "Mount" => mountModule.recoloringData,
                _ => coreModule.recoloringData,
            };
        }

        public void setSectionColors(string section, RecoloringData[] colors)
        {
            if (section == "Nose") noseModule.setSectionColors(colors);
            else if (section == "Core") coreModule.setSectionColors(colors);
            else if (section == "Mount") mountModule.setSectionColors(colors);
        }

        public TextureSet getSectionTexture(string section)
        {
            return section switch
            {
                "Nose" => noseModule.textureSet,
                "Core" => coreModule.textureSet,
                "Mount" => mountModule.textureSet,
                _ => coreModule.textureSet,
            };
        }

        //IContainerVolumeContributor override
        public ContainerContribution[] getContainerContributions()
        {
            ContainerContribution[] cts;
            ContainerContribution ct0 = GetCC("nose", noseContainerIndex, noseModule.moduleVolume * 1000f);
            ContainerContribution ct1 = GetCC("core", coreContainerIndex, coreModule.moduleVolume * 1000f);
            ContainerContribution ct2 = GetCC("mount", mountContainerIndex, mountModule.moduleVolume * 1000f);
            cts = new ContainerContribution[3] { ct0, ct1, ct2 };
            return cts;
        }

        private ContainerContribution GetCC(string name, int index, float vol)
        {
            return new ContainerContribution(name, index, vol);
        }

        #endregion IRecolorable and IContainerVolumeContributor Overrides

        #region Custom Update Methods

        /// <summary>
        /// Initialization method.  Sets up model modules, loads their configs from the input config node.  Does all initial linking of part-modules.<para/>
        /// Does NOT set up their UI interaction -- that is all handled during OnStart()
        /// </summary>
        private void Initialize()
        {
            if (initialized) { return; }
            initialized = true;

            prevDiameter = currentDiameter;
            if (lengthWidth)
            {
                prevLength = currentLength;
            }

            noseNodeNames = ROLUtils.parseCSV(noseManagedNodes);
            coreNodeNames = ROLUtils.parseCSV(coreManagedNodes);
            mountNodeNames = ROLUtils.parseCSV(mountManagedNodes);

            //model-module setup/initialization
            ConfigNode node = ROLConfigNodeUtils.parseConfigNode(configNodeData);

            //list of CORE model nodes from config
            //each one may contain multiple 'model=modelDefinitionName' entries
            //but must contain no more than a single 'variant' entry.
            //if no variant is specified, they are added to the 'Default' variant.
            List<ModelDefinitionLayoutOptions> coreDefList = new List<ModelDefinitionLayoutOptions>();
            foreach (ConfigNode n in node.GetNodes("CORE"))
            {
                string variantName = n.ROLGetStringValue("variant", "Default");
                coreDefs = ROLModelData.getModelDefinitionLayouts(n.ROLGetStringValues("model"));
                coreDefList.AddUniqueRange(coreDefs);
                ModelDefinitionVariantSet mdvs = GetVariantSet(variantName);
                mdvs.AddModels(coreDefs);
            }
            coreDefs = coreDefList.ToArray();

            //model defs - brought here so we can capture the array rather than the config node+method call
            noseDefs = ROLModelData.getModelDefinitions(node.GetNodes("NOSE"));
            mountDefs = ROLModelData.getModelDefinitions(node.GetNodes("MOUNT"));

            noseModule = new ROLModelModule<ModuleROTank>(part, this, ROLUtils.GetRootTransform(part, "ModularPart-NOSE"), ModelOrientation.TOP, nameof(currentNose), null, nameof(currentNoseTexture), nameof(noseModulePersistentData));
            noseModule.name = "ModuleROTank-Nose";
            noseModule.getSymmetryModule = m => m.noseModule;
            noseModule.getValidOptions = () => noseDefs;

            coreModule = new ROLModelModule<ModuleROTank>(part, this, ROLUtils.GetRootTransform(part, "ModularPart-CORE"), ModelOrientation.CENTRAL, nameof(currentCore), null, nameof(currentCoreTexture), nameof(coreModulePersistentData));
            coreModule.name = "ModuleROTank-Core";
            coreModule.getSymmetryModule = m => m.coreModule;
            coreModule.getValidOptions = () => GetVariantSet(currentVariant).definitions;

            mountModule = new ROLModelModule<ModuleROTank>(part, this, ROLUtils.GetRootTransform(part, "ModularPart-MOUNT"), ModelOrientation.BOTTOM, nameof(currentMount), null, nameof(currentMountTexture), nameof(mountModulePersistentData));
            mountModule.name = "ModuleROTank-Mount";
            mountModule.getSymmetryModule = m => m.mountModule;
            mountModule.getValidOptions = () => mountDefs;

            noseModule.volumeScalar = volumeScalingPower;
            coreModule.volumeScalar = volumeScalingPower;
            mountModule.volumeScalar = volumeScalingPower;

            //set up the model lists and load the currently selected model
            noseModule.setupModelList(noseDefs);
            coreModule.setupModelList(coreDefs);
            mountModule.setupModelList(mountDefs);
            coreModule.setupModel();
            noseModule.setupModel();
            mountModule.setupModel();
        }

        /// <summary>
        /// Initialize the UI controls, including default values, and specifying delegates for their 'onClick' methods.<para/>
        /// All UI based interaction code will be defined/run through these delegates.
        /// </summary>
        public void InitializeUI()
        {
            //set up the core variant UI control
            string[] variantNames = ROLUtils.getNames(variantSets.Values, m => m.variantName);
            this.ROLupdateUIChooseOptionControl(nameof(currentVariant), variantNames, variantNames);
            Fields[nameof(currentVariant)].guiActiveEditor = variantSets.Count > 1;

            Fields[nameof(currentVariant)].uiControlEditor.onFieldChanged = (a, b) =>
            {
                //TODO find variant set for the currently enabled core model
                //query the index from that variant set
                ModelDefinitionVariantSet prevMdvs = GetVariantSet(coreModule.definition.name);
                //this is the index of the currently selected model within its variant set
                int previousIndex = prevMdvs.IndexOf(coreModule.layoutOptions);
                //grab ref to the current/new variant set
                ModelDefinitionVariantSet mdvs = GetVariantSet(currentVariant);
                //and a reference to the model from same index out of the new set ([] call does validation internally for IAOOBE)
                ModelDefinitionLayoutOptions newCoreDef = mdvs[previousIndex];
                //now, call model-selected on the core model to update for the changes, including symmetry counterpart updating.
                this.ROLactionWithSymmetry(m =>
                {
                    if (lengthWidth)
                        m.SetModelFromDimensions();
                    else
                        m.coreModule.modelSelected(newCoreDef.definition.name);
                    ModelChangedHandler(true);
                });
                MonoUtilities.RefreshPartContextWindow(part);
            };

            Fields[nameof(currentDiameter)].uiControlEditor.onFieldChanged =
            Fields[nameof(currentDiameter)].uiControlEditor.onSymmetryFieldChanged = OnDiameterChanged;

            Fields[nameof(currentLength)].uiControlEditor.onFieldChanged =
            Fields[nameof(currentLength)].uiControlEditor.onSymmetryFieldChanged = OnLengthChanged;

            Fields[nameof(currentVScale)].uiControlEditor.onFieldChanged =
            Fields[nameof(currentVScale)].uiControlEditor.onSymmetryFieldChanged = (a, b) =>
            {
                ModelChangedHandler(true);
            };

            Fields[nameof(currentNose)].uiControlEditor.onFieldChanged =
            Fields[nameof(currentNose)].uiControlEditor.onSymmetryFieldChanged = OnModelSelectionChanged;
            Fields[nameof(currentCore)].uiControlEditor.onFieldChanged =
            Fields[nameof(currentCore)].uiControlEditor.onSymmetryFieldChanged = OnModelSelectionChanged;
            Fields[nameof(currentMount)].uiControlEditor.onFieldChanged =
            Fields[nameof(currentMount)].uiControlEditor.onSymmetryFieldChanged = OnModelSelectionChanged;

            //------------------MODEL DIAMETER / LENGTH SWITCH UI INIT---------------------//
            this.ROLupdateUIFloatEditControl(nameof(currentDiameter), minDiameter, maxDiameter, diameterLargeStep, diameterSmallStep, diameterSlideStep);
            this.ROLupdateUIFloatEditControl(nameof(currentLength), minLength, maxLength, diameterLargeStep, diameterSmallStep, diameterSlideStep);

            Fields[nameof(currentDiameter)].guiActiveEditor = maxDiameter != minDiameter;
            Fields[nameof(currentLength)].guiActiveEditor = lengthWidth && maxLength != minLength;
            Fields[nameof(currentVScale)].guiActiveEditor = enableVScale && !lengthWidth;
            Events[nameof(ResetModel)].guiActiveEditor = !lengthWidth;

            //------------------MODULE TEXTURE SWITCH UI INIT---------------------//
            Fields[nameof(currentNoseTexture)].uiControlEditor.onFieldChanged = noseModule.textureSetSelected;
            Fields[nameof(currentCoreTexture)].uiControlEditor.onFieldChanged = coreModule.textureSetSelected;
            Fields[nameof(currentMountTexture)].uiControlEditor.onFieldChanged = mountModule.textureSetSelected;

            if (HighLogic.LoadedSceneIsEditor)
                GameEvents.onEditorShipModified.Add(OnEditorVesselModified);
        }

        private void OnModelSelectionChanged(BaseField f, object o)
        {
            if (f.name == Fields[nameof(currentMount)].name) mountModule.modelSelected(currentMount);
            else if (f.name == Fields[nameof(currentCore)].name) coreModule.modelSelected(currentCore);
            else if (f.name == Fields[nameof(currentNose)].name) noseModule.modelSelected(currentNose);

            ModelChangedHandler(true);
            MonoUtilities.RefreshPartContextWindow(part);
        }

        private void OnDiameterChanged(BaseField f, object o)
        {
            // KSP 1.7.3 bug, symmetry invocations will have o=newValue instead of previousValue
            if ((float)f.GetValue(this) == prevDiameter) return;
            if (lengthWidth)
            {
                ValidateLength();
                SetModelFromDimensions();
            }
            ModelChangedHandler(true);
            prevDiameter = currentDiameter;
        }

        private void OnLengthChanged(BaseField f, object o) 
        {
            if ((float)f.GetValue(this) == prevLength) return;
            ValidateLength();
            SetModelFromDimensions();
            ModelChangedHandler(true);
            prevLength = currentLength;
        }

        /// <summary>
        /// Update the scale and position values for all currently configured models.  Does no validation, only updates positions.<para/>
        /// After calling this method, all models will be scaled and positioned according to their internal position/scale values and the orientations/offsets defined in the models.
        /// </summary>
        public void UpdateModulePositions()
        {
            //scales for modules depend on the module above/below them
            //first set the scale for the core module -- this depends directly on the UI specified 'diameter' value.
            if (lengthWidth)
                coreModule.setScaleForHeightAndDiameter(currentLength, currentDiameter);
            else
                coreModule.RescaleToDiameter(currentDiameter, coreModule.definition.diameter, currentVScale);

            noseModule.RescaleToDiameter(coreModule.moduleUpperDiameter, noseModule.moduleLowerDiameter / noseModule.moduleHorizontalScale, currentVScale);
            mountModule.RescaleToDiameter(coreModule.moduleLowerDiameter, mountModule.moduleUpperDiameter / mountModule.moduleHorizontalScale, currentVScale);

            float totalHeight = noseModule.moduleHeight + coreModule.moduleHeight + mountModule.moduleHeight;

            //position of each module is set such that the vertical center of the models is at part origin/COM
            float pos = totalHeight * 0.5f;//abs top of model
            pos -= noseModule.moduleHeight;//bottom of nose model
            noseModule.SetPosition(pos);
            pos -= coreModule.moduleHeight * 0.5f;//center of 'core' model
            coreModule.SetPosition(pos);
            pos -= coreModule.moduleHeight * 0.5f;//bottom of 'core' model
            mountModule.SetPosition(pos);
        }

        public void UpdateModelMeshes()
        {
            noseModule.UpdateModelScalesAndLayoutPositions();
            coreModule.UpdateModelScalesAndLayoutPositions();
            mountModule.UpdateModelScalesAndLayoutPositions();
        }


        /// <summary>
        /// Update the cached modifiedMass field values.  Used with stock mass modifier interface.<para/>
        /// </summary>
        public void UpdateMass()
        {
            modifiedMass = coreModule.moduleMass + noseModule.moduleMass + mountModule.moduleMass;
        }

        /// <summary>
        /// Update the cached modifiedCost field values.  Used with stock cost modifier interface.<para/>
        /// </summary>
        public void UpdateCost()
        {
            modifiedCost = coreModule.moduleCost + noseModule.moduleCost + mountModule.moduleCost;
        }

        /// <summary>
        /// Updates all dimensions for the PAW and tooling.
        /// </summary>
        public void UpdateDimensions()
        {
            // If mount module is originally designed as a bottom (mount) module, refer to upper diameter.  If it is anything repurposed, use its max diameter.
            float mountMaxDiam = mountModule.definition.shouldInvert(ModelOrientation.BOTTOM) ? Math.Max(mountModule.moduleLowerDiameter, mountModule.moduleUpperDiameter) : mountModule.moduleUpperDiameter;
            float noseMaxDiam = Math.Max(noseModule.moduleLowerDiameter, noseModule.moduleUpperDiameter);
            totalTankLength = GetTotalHeight();
            largestDiameter = Math.Max(currentDiameter, Math.Max(noseMaxDiam, mountMaxDiam));
            ROLLog.debug($"UpdateDimensions() currentMount: {currentMount}  Largest Diameter: {largestDiameter}.  Total Tank length: {totalTankLength}");
        }

        /// <summary>
        /// Update the attach nodes for the current model-module configuration.
        /// The 'nose' module is responsible for updating of upper attach nodes, while the 'mount' module is responsible for lower attach nodes.
        /// Also includes updating of 'interstage' nose/mount attach nodes.
        /// Also includes updating of surface-attach node position.
        /// Also includes updating of any parts that are surface attached to this part.
        /// </summary>
        /// <param name="userInput"></param>
        public void UpdateAttachNodes(bool userInput)
        {
            //update the standard top and bottom attach nodes, using the node position(s) defined in the nose and mount modules
            noseModule.UpdateAttachNode("top", ModelOrientation.TOP, userInput);
            mountModule.UpdateAttachNode("bottom", ModelOrientation.BOTTOM, userInput);

            //update the model-module specific attach nodes, using the per-module node definitions from the part
            noseModule.updateAttachNodeBody(noseNodeNames, userInput);
            coreModule.updateAttachNodeBody(coreNodeNames, userInput);
            mountModule.updateAttachNodeBody(mountNodeNames, userInput);

            // Update the Nose Interstage Node
            float y = noseModule.modulePosition + noseModule.moduleVerticalScale;
            int nodeSize = Mathf.RoundToInt(coreModule.moduleDiameter) + 1;
            Vector3 pos = new Vector3(0, y, 0);
            ROLSelectableNodes.updateNodePosition(part, noseInterstageNode, pos);
            if (part.FindAttachNode(noseInterstageNode) is AttachNode noseInterstage)
                ROLAttachNodeUtils.updateAttachNodePosition(part, noseInterstage, pos, Vector3.up, userInput, nodeSize);

            // Update the Mount Interstage Node
            y = mountModule.modulePosition + mountModule.moduleVerticalScale;
            nodeSize = Mathf.RoundToInt(coreModule.moduleDiameter) + 1;
            pos = new Vector3(0, y, 0);
            ROLSelectableNodes.updateNodePosition(part, mountInterstageNode, pos);
            if (part.FindAttachNode(mountInterstageNode) is AttachNode mountInterstage)
                ROLAttachNodeUtils.updateAttachNodePosition(part, mountInterstage, pos, Vector3.down, userInput, nodeSize);

            //update surface attach node position, part position, and any surface attached children
            if (part.srfAttachNode is AttachNode surfaceNode)
                coreModule.updateSurfaceAttachNode(surfaceNode, prevDiameter, userInput);
        }

        /// <summary>
        /// Return the total height of this part in its current configuration.  This will be the distance from the bottom attach node to the top attach node, and may not include any 'extra' structure. TOOLING
        /// </summary>
        /// <returns></returns>
        private float GetTotalHeight()
        {
            float totalHeight = noseModule.moduleHeight + mountModule.moduleHeight;
            totalHeight += (currentCore.Contains("Booster")) ? coreModule.moduleActualHeight : coreModule.moduleHeight;
            return totalHeight;
        }

        /// <summary>
        /// Update the UI visibility for the currently available selections.<para/>
        /// Will hide/remove UI fields for slots with only a single option (models, textures, layouts).
        /// </summary>
        public void UpdateAvailableVariants()
        {
            noseModule.updateSelections();
            coreModule.updateSelections();
            mountModule.updateSelections();
        }

        /// <summary>
        /// Calls the generic ROT procedural drag-cube updating routines.  Will update the drag cubes for whatever the current model state is.
        /// </summary>
        private void UpdateDragCubes()
        {
            ROLModInterop.OnPartGeometryUpdate(part, true);
        }

        private void SetModelFromDimensions()
        {
            currentVScale = 0.0f;

            if (!lengthWidth) return;

            // Round to nearest 0.5: Multiply by 2, round to nearest int, divide by 2.
            float dimRatio = currentLength / currentDiameter;
            float modelRatio = Mathf.Round(dimRatio * 2) / 2;
            modelRatio = Mathf.Clamp(modelRatio, minModelRatio, maxModelRatio);

            string ratioName = $"{modelRatio:0.0}";
            string s = $"{ratioName}x-{currentVariant}";
            ROLLog.debug($"dimRatio: {dimRatio}, modelRatio: {modelRatio}, {ratioName}x-{currentVariant}");

            currentVScale = (dimRatio / modelRatio) - 1;
            if (coreModule.modelName != s)
                coreModule.modelSelected(s);
        }

        #nullable enable
        private float ModuleEffectiveLength(ROLModelModule<ModuleROTank>? module) =>
            (module is null) ? 0 : module.definition.effectiveLength * currentDiameter / coreModule.definition.diameter;
        #nullable disable

        private float DomeLength => currentDiameter / 2;
        private float NoseEffectiveLength => ModuleEffectiveLength(noseModule);
        private float MountEffectiveLength => ModuleEffectiveLength(mountModule);
        private float EffectiveCylinderLength() //=> currentLength + NoseEffectiveLength + MountEffectiveLength - DomeLength;
        {
            float effectiveLength = currentLength + NoseEffectiveLength + MountEffectiveLength - DomeLength;
            ROLLog.debug($"EffectiveLength() horScale: {currentDiameter / coreModule.definition.diameter}.  Nose: {NoseEffectiveLength:F1}, mount: {MountEffectiveLength:F1}, core: {currentLength:F1}, dome: {DomeLength:F1}, result: {effectiveLength}");
            return effectiveLength;
        }

        private float CalcMinLength()
        {
            float min = Math.Max(0.1f, DomeLength - (NoseEffectiveLength + MountEffectiveLength));
            return Mathf.Ceil(min / diameterSlideStep) * diameterSlideStep;
        }

        private void ValidateLength()
        {
            float minL = Mathf.Max(minLength, CalcMinLength());
            this.ROLupdateUIFloatEditControl(nameof(currentLength), minL, maxLength, diameterLargeStep, diameterSmallStep, diameterSlideStep);
            currentLength = Mathf.Max(currentLength, minL);
        }

        private void UpdateTankVolume(bool lw)
        {
            if (!lw)
            {
                float totalVol = noseModule.moduleVolume + coreModule.moduleVolume + mountModule.moduleVolume;
                SendVolumeChangedEvent(totalVol);
                return;
            }

            // Get the additional volume from the nose and mounts *beyond what is provided by the tank length extension*
            // Nose & Mount Diameters are the diameter of the model at the point of attachment.
            // Inversion handles a particular model that mounts in either orientation, and its values are for the orientation specified.
            float noseDiameter = noseModule.definition.shouldInvert(ModelOrientation.TOP) ? noseModule.definition.upperDiameter : noseModule.definition.lowerDiameter;
            float noseScale = Mathf.Pow(currentDiameter / noseDiameter, 3);

            float mountDiameter = mountModule.definition.shouldInvert(ModelOrientation.BOTTOM) ? mountModule.definition.lowerDiameter : mountModule.definition.upperDiameter;
            float mountScale = Mathf.Pow(currentDiameter / mountDiameter, 3);

            float noseAdditionalVol = noseScale * noseModule.definition.additionalVolume * 1000f;
            float mountAdditionalVol = mountScale * mountModule.definition.additionalVolume * 1000f;

            // Calculate the volume of the main tank
            float r = currentDiameter / 2;
            float effectiveVolume = (ROLUtils.EllipsoidVolume(r, r, r/2) + ROLUtils.CylinderVolume(r, EffectiveCylinderLength())) * 1000f;
            effectiveVolume += noseAdditionalVol + mountAdditionalVol;
            ROLLog.debug($"UpdateTankVolume() Nose scale: {noseScale:F3} -> Vol: {noseAdditionalVol:F1}.  Mount scale: {mountScale:F3} -> Vol: {mountAdditionalVol:F1}.  Total volume: {effectiveVolume}");

            ROLModInterop.RealFuelsVolumeUpdate(part, effectiveVolume);
        }

        public void SendVolumeChangedEvent(float newVol)
        {
            var data = new BaseEventDetails(BaseEventDetails.Sender.USER);
            data.Set<string>("volName", "Tankage");
            data.Set<double>("newTotalVolume", newVol);
            part.SendEvent("OnPartVolumeChanged", data, 0);
        }

        #endregion ENDREGION - Custom Update Methods

        #region GUI

        private void OnGUI()
        {
            GUI.depth = 0;

            Action windows = delegate { };
            foreach (var window in AbstractWindow.Windows.Values)
            {
                windows += window.Draw;
            }
            windows.Invoke();
        }

        private void HideGUI()
        {
            if (dimWindow != null)
            {
                dimWindow.Hide();
                dimWindow = null;
            }
        }

        private void OnSceneChange(GameScenes _) => HideGUI();

        public void EditDimensions()
        {
            if (dimWindow != null)
                HideGUI();
            else 
            {
                dimWindow = new DimensionWindow(this);
                dimWindow.Show();
            }
        }

        #endregion GUI

    }
}
