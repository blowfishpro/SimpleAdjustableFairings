using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SimpleAdjustableFairings
{
    public class ModuleSimpleAdjustableFairing : PartModule, IScalarModule, IPartCoMModifier, ISerializationCallbackReceiver
    {
        public const string FAIRING_ROOT_TRANSFORM_NAME = "FairingRoot";

        #region Loadable Fields

        [KSPField]
        public string moduleID;

        [KSPField]
        public float segmentLength;

        [KSPField]
        public Vector3 axis = Vector3.up;

        [KSPField]
        public Vector3 shieldingCenter = Vector3.zero;

        [KSPField]
        public float shieldingBaseRadius = 0f;

        [KSPField]
        public int numSlices = 2;

        [KSPField]
        public int maxSegments = 10;

        [KSPField]
        public Vector3 editorOpenOffset;

        [KSPField]
        public float deploySpeed = 20f;

        [KSPField]
        public float deployAngularSpeed = 1f;

        [KSPField]
        public bool deployOnStage = true;

        [KSPField]
        public float scale = 1f;

        #endregion

        #region Persistent Fields

        [KSPField(isPersistant = true)]
        public bool deployed = false;

        [UI_FloatRange(scene = UI_Scene.Editor, minValue = 0f, maxValue = 10f, stepIncrement = 1f)]
        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Num Segments")]
        public float numSegments = 0f;

        [UI_Toggle(scene = UI_Scene.Editor, disabledText = "Opaque", enabledText = "Transparent")]
        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Fairing")]
        public bool transparentEditor = true;

        [UI_Toggle(scene = UI_Scene.Editor, disabledText = "Closed", enabledText = "Open")]
        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Fairing")]
        public bool openFairing = false;

        [UI_Toggle(scene = UI_Scene.All, disabledText = "No", enabledText = "Yes")]
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Auto-Deploy Fairing")]
        public bool autoDeploy = true;

        [UI_FloatRange(minValue = 0f, maxValue = 100f, stepIncrement = 5f, scene = UI_Scene.All)]
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Autodeploy Altitude (km)")]
        public float deployAltitude = float.NaN;

        #endregion

        #region Private fields

        private List<FairingSlice> slices = new List<FairingSlice>();
        private ModuleCargoBay cargoBay;

        private EventData<float, float> fairingDeployStart = new EventData<float, float>("FairingDeployStart");
        private EventData<float> fairingDeployEnd = new EventData<float>("FairingDeployEnd");

        [SerializeField]
        private string serializedData;

        #endregion

        #region Properties

        public ModelData WallData { get; private set; }
        public ModelData ConeData { get; private set; }

        public Transform ModelRootTransform { get; private set; }
        public Transform FairingRootTransform { get; private set; }
        public Transform PrefabWallTransform { get; private set; }
        public Transform PrefabConeTransform { get; private set; }

        public Vector3 SegmentOffset => axis * segmentLength;

        public bool FairingCollidersEnabled => !HighLogic.LoadedSceneIsEditor;

        #endregion

        #region Events

        [KSPEvent(guiActive = true, guiName = "Deploy")]
        public void DeployEvent()
        {
            Deploy();
        }

        public override void OnActive()
        {
            base.OnActive();

            if (deployOnStage) Deploy();
        }

        #endregion

        #region Setup

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);

            ConfigNode wallDataNode = node.GetNode("WALL");
            if (wallDataNode != null)
                WallData = ConfigNode.CreateObjectFromConfig<ModelData>(wallDataNode);

            ConfigNode coneDataNode = node.GetNode("CONE");
            if (coneDataNode != null)
                ConeData = ConfigNode.CreateObjectFromConfig<ModelData>(coneDataNode);
        }

        public override void OnIconCreate()
        {
            base.OnIconCreate();

            if (FindTransforms())
                SetupForIcon();
        }

        public override void OnStartFinished(StartState state)
        {
            base.OnStartFinished(state);

            if (!FindTransforms()) return;
            FindCargoBay();

            HidePrefabTransforms();

            if (deployed)
            {
                HideDeployEvent();
            }
            else
            {
                if (state == StartState.Editor && part.parent == null && EditorLogic.RootPart != part)
                {
                    part.OnEditorAttach += SetupFairingOnAttach;
                }
                else
                {
                    SetupFairing();
                }

                CalculateAutodeployAltitude();
            }
            UpdateDeployAltitudeVisibility();

            if (state == StartState.Editor)
            {
                SetupEditorGui();
            }
            else
            {
                RenderProceduralDragCubes();
                UpdateFAR();
                IgnoreColliders();
            }
        }

        #endregion

        #region Serialization

        public void OnBeforeSerialize()
        {
            ConfigNode node = new ConfigNode("SERIALIZED_DATA");
            if (WallData != null) node.AddNode("wallData", ConfigNode.CreateConfigFromObject(WallData));
            if (ConeData != null) node.AddNode("coneData", ConfigNode.CreateConfigFromObject(ConeData));
            serializedData = node.ToString();
        }

        public void OnAfterDeserialize()
        {
            if (serializedData == null)
            {
                this.LogWarning("serialized data is null!");
                return;
            }

            ConfigNode node = ConfigNode.Parse(serializedData);
            ConfigNode node2 = node.GetNode("SERIALIZED_DATA");

            if (node2 == null)
            {
                this.LogWarning("no serialized data found on node!");
                return;
            }

            ConfigNode wallDataNode = node2.GetNode("wallData");
            if (wallDataNode != null) WallData = ConfigNode.CreateObjectFromConfig<ModelData>(wallDataNode);
            else this.LogWarning("no wall data found in node!");

            ConfigNode coneDataNode = node2.GetNode("coneData");
            if (coneDataNode != null) ConeData = ConfigNode.CreateObjectFromConfig<ModelData>(coneDataNode);
            else this.LogWarning("no cone data found in node!");
        }

        #endregion

        #region Callbacks

        private void SetupFairingOnAttach()
        {
            part.OnEditorAttach -= SetupFairingOnAttach;
            SetupFairing();
            UpdateFAR();
        }

        private void OnSegmentNumberChange(BaseField field, object oldValue)
        {
            UpdateSegments();
            UpdateCargoBay();
            part.ModifyCoM();
            part.RefreshHighlighter();
            UpdateFAR();
        }

        private void OnToggleTransparent(BaseField field, object oldValue)
        {
            UpdateTransparency();
        }

        private void OnToggleOpen(BaseField field, object oldValue)
        {
            if (openFairing)
            {
                slices.ForEach(slice => slice.Open());
            }
            else
            {
                slices.ForEach(slice => slice.Close());
            }
        }

        private void OnToggleAutodeploy(BaseField field, object oldValue)
        {
            UpdateDeployAltitudeVisibility();
        }

        #endregion

        #region Update

        public virtual void FixedUpdate()
        {
            CheckAutodeploy();
        }

        #endregion

        #region Interface Methods

        public string ScalarModuleID => moduleID;
        public float GetScalar => deployed ? 1f : 0f;
        public bool CanMove => !deployed;
        public EventData<float, float> OnMoving => fairingDeployStart;
        public EventData<float> OnStop => fairingDeployEnd;

        public void SetScalar(float f) { }
        public void SetUIRead(bool state) { }
        public void SetUIWrite(bool state) { }
        public bool IsMoving() => false;

        public float GetModuleMass(float defaultMass, ModifierStagingSituation situation)
        {
            if (situation == ModifierStagingSituation.UNSTAGED || (situation == ModifierStagingSituation.CURRENT && !deployed))
            {
                return CalculateFairingMass();
            }
            else
            {
                return 0f;
            }
        }

        public ModifierChangeWhen GetModuleMassChangeWhen() => ModifierChangeWhen.STAGED;

        public Vector3 GetModuleCoM()
        {
            if (deployed) return Vector3.zero;

            Vector3 CoM = Vector3.zero;
            float mass = 0f;

            foreach (FairingSlice slice in slices)
            {
                float sliceMass = slice.Mass;
                CoM += slice.CalculatePartRelativeCoM() * sliceMass;
                mass += sliceMass;
            }

            CoM /= mass;
            
            return CoM;
        }

        #endregion

        #region Private Methods

        private bool FindTransforms()
        {
            bool result = true;
            ModelRootTransform = part.FindModelTransform("model");

            if (ModelRootTransform == null)
            {
                this.LogError("Root transform could not be found!");
                result = false;
            }

            if (WallData == null)
            {
                this.LogError("wallData is null, cannot find transform!");
                result = false;
            }
            else
            {
                PrefabWallTransform = part.FindModelTransform(WallData.transformName);
                if (PrefabWallTransform == null)
                {
                    this.LogError($"Could not find wall transform named '{WallData.transformName}'");
                    result = false;
                }
            }

            if (ConeData == null)
            {
                this.LogError("coneData is null, cannot find transform!");
                result = false;
            }
            else
            {
                PrefabConeTransform = part.FindModelTransform(ConeData.transformName);
                if (PrefabConeTransform == null)
                {
                    this.LogError($"Could not find cone transform named '{ConeData.transformName}'");
                    result = false;
                }
            }

            return result;
        }

        private void SetupForIcon()
        {
            PrefabConeTransform.localPosition = (SegmentOffset  + ConeData.rootOffset) / scale;
            PrefabWallTransform.localPosition = WallData.rootOffset / scale;
        }

        private void FindCargoBay()
        {
            cargoBay = part.Modules.OfType<ModuleCargoBay>().Where(module => module.DeployModuleIndex == part.Modules.IndexOf(this)).FirstOrDefault();
        }

        private void HidePrefabTransforms()
        {
            PrefabConeTransform.gameObject.SetActive(false);
            PrefabWallTransform.gameObject.SetActive(false);
        }

        private void HideDeployEvent()
        {
            Events[nameof(DeployEvent)].guiActive = false;
            Fields[nameof(autoDeploy)].guiActive = false;
            Fields[nameof(deployAltitude)].guiActive = false;
        }

        private void SetupFairing()
        {
            // If we are duplicating in the editor, there will be some leftovers
            // Easier to just get rid of them rather than try to rebuild the hierarchy
            GameObject oldFairing = part.FindModelTransform(FAIRING_ROOT_TRANSFORM_NAME)?.gameObject;
            if (oldFairing != null) Destroy(oldFairing);

            GameObject fairingRootGO = new GameObject(FAIRING_ROOT_TRANSFORM_NAME);
            FairingRootTransform = fairingRootGO.transform;
            FairingRootTransform.NestToParent(ModelRootTransform);

            slices.Clear();
            for (int i = 0; i < numSlices; i++)
            {
                slices.Add(new FairingSlice(this, i));
            }

            UpdateSegments();
            UpdateTransparency();
            UpdateCargoBay();
            part.ModifyCoM();
        }

        private void SetupEditorGui()
        {
            UI_FloatRange numSegmentsControl = this.GetUIControl<UI_FloatRange>(nameof(numSegments));
            numSegmentsControl.onFieldChanged = OnSegmentNumberChange;
            numSegmentsControl.maxValue = Math.Max(maxSegments, numSegments);
            this.GetUIControl(nameof(transparentEditor)).onFieldChanged = OnToggleTransparent;
            this.GetUIControl(nameof(openFairing)).onFieldChanged = OnToggleOpen;
            this.GetUIControl(nameof(autoDeploy)).onFieldChanged = OnToggleAutodeploy;
        }

        private void CalculateAutodeployAltitude()
        {
            if (deployed)
            {
                deployAltitude = 0f;
                return;
            }

            CelestialBody home = Planetarium.fetch?.Home;
            if (home == null)
                this.LogError($"[{part.name} {this.GetType().Name}] Cannot find home celestial body to set altitude from");
            
            UI_FloatRange deployAltitudeControl = this.GetUIControl<UI_FloatRange>(nameof(deployAltitude));

            deployAltitudeControl.minValue = 0f;
            deployAltitudeControl.maxValue = (float?)home?.atmosphereDepth / 1000f ?? 200f;

            if (float.IsNaN(deployAltitude))
            {
                if (home != null)
                {
                    deployAltitude = Mathf.Round((float)home.atmosphereDepth * 0.75f / 1000f / 5f) * 5f;
                }
                else
                {
                    autoDeploy = false;
                    deployAltitude = 100f;
                }
            }
            else if (deployAltitude < 0f)
            {
                deployAltitude = 0f;
            }
        }

        private void IgnoreColliders()
        {
            CollisionManager.IgnoreCollidersOnVessel(vessel, FairingRootTransform.GetComponentsInChildren<Collider>());
        }

        private void UpdateSegments()
        {
            slices.ForEach(slice => slice.NumSegments = (int)numSegments);
        }

        private void UpdateTransparency()
        {
            if (transparentEditor && HighLogic.LoadedSceneIsEditor)
            {
                slices.ForEach(slice => slice.MakeTransparent());
            }
            else
            {
                slices.ForEach(slice => slice.MakeOpaque());
            }
        }

        private void UpdateCargoBay()
        {
            if (cargoBay == null) return;

            float addedRadius = numSegments * segmentLength * 0.5f;
            cargoBay.lookupCenter = axis * addedRadius + shieldingCenter;
            cargoBay.lookupRadius = addedRadius + shieldingBaseRadius;

#if DEBUG
            this.LogInfo($"Cargo bay center: {cargoBay.lookupCenter}, radius: {cargoBay.lookupRadius}");
#endif
        }

        private void RenderProceduralDragCubes()
        {
            DragCube newCube = DragCubeSystem.Instance.RenderProceduralDragCube(part);
            part.DragCubes.ClearCubes();
            part.DragCubes.Cubes.Add(newCube);
            part.DragCubes.ResetCubeWeights();
        }

        private void UpdateFAR()
        {
            part.SendMessage("GeometryPartModuleRebuildMeshData");
        }

        private float CalculateFairingMass()
        {
            // Don't use linq here as it runs every frame
            // foreach ok because it's directly on a List<T>
            float fairingMass = 0f;
            foreach (FairingSlice slice in slices)
            {
                fairingMass += slice.Mass;
            }
            return fairingMass;
        }

        private void Deploy()
        {
            if (deployed) return;

            OnMoving.Fire(0f, 1f);

            slices.ForEach(slice => slice.Detach());

            part.ModifyCoM();
            RenderProceduralDragCubes();
            UpdateFAR();

            OnStop.Fire(1f);

            HideDeployEvent();

            deployed = true;

            GameEvents.onVesselWasModified.Fire(vessel);
        }

        private void UpdateDeployAltitudeVisibility()
        {
            BaseField deployAltitudeField = Fields[nameof(deployAltitude)];

            if (deployed)
            {
                deployAltitudeField.guiActive = false;
                deployAltitudeField.guiActiveEditor = false;

                BaseField autodeployField = Fields[nameof(autoDeploy)];

                autodeployField.guiActive = false;
                autodeployField.guiActiveEditor = false;
            }
            else
            {
                deployAltitudeField.guiActive = autoDeploy;
                deployAltitudeField.guiActiveEditor = autoDeploy;
            }
        }

        private void CheckAutodeploy()
        {
            if (deployed || !autoDeploy || HighLogic.LoadedSceneIsEditor || !part.started) return;

            if (deployAltitude * 1000f < vessel.altitude) Deploy();
        }

        #endregion
    }
}
