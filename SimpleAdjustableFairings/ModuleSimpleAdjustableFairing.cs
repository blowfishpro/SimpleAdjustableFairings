using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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

        private ModelData wallData;
        private ModelData coneData;

        private ResolvedModelData coneObjectPrefab;
        private ResolvedModelData wallObjectPrefab;

        private GameObject modelRoot;
        private GameObject fairingRoot;

        private readonly List<FairingSlice> slices = new List<FairingSlice>();
        private ModuleCargoBay cargoBay;

        [SerializeField]
        private string serializedData;

        #endregion

        #region Properties

        private Vector3 SegmentOffset => axis * segmentLength;

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

        #region Actions

        [SuppressMessage("Style", "IDE0060", Justification = "Required by KSP")]
        [KSPAction("Deploy")]
        public void DeployAction(KSPActionParam param)
        {
            Deploy();
        }

        #endregion

        #region Setup

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);

            ConfigNode wallDataNode = node.GetNode("WALL");
            if (wallDataNode != null)
                wallData = ConfigNode.CreateObjectFromConfig<ModelData>(wallDataNode);

            ConfigNode coneDataNode = node.GetNode("CONE");
            if (coneDataNode != null)
                coneData = ConfigNode.CreateObjectFromConfig<ModelData>(coneDataNode);
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

            if (state == StartState.Editor)
            {
                coneObjectPrefab.gameObject.SetCollidersEnabled(false);
                wallObjectPrefab?.gameObject.SetCollidersEnabled(false);
            }

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
            if (wallData != null) node.AddNode("wallData", ConfigNode.CreateConfigFromObject(wallData));
            if (coneData != null) node.AddNode("coneData", ConfigNode.CreateConfigFromObject(coneData));
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
            if (wallDataNode != null) wallData = ConfigNode.CreateObjectFromConfig<ModelData>(wallDataNode);
            else this.LogWarning("no wall data found in node!");

            ConfigNode coneDataNode = node2.GetNode("coneData");
            if (coneDataNode != null) coneData = ConfigNode.CreateObjectFromConfig<ModelData>(coneDataNode);
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
            UpdateOpen();
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
        public EventData<float, float> OnMoving { get; } = new EventData<float, float>("FairingDeployStart");
        public EventData<float> OnStop { get; } = new EventData<float>("FairingDeployEnd");

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
                Vector3 worldCoM = slice.SliceRootObject.transform.TransformPoint(slice.CalculateCoM());
                CoM += part.partTransform.InverseTransformPoint(worldCoM) * sliceMass;
                mass += sliceMass;
            }

            return (mass > 0) ? CoM / mass : Vector3.zero;
        }

        #endregion

        #region Private Methods

        private bool FindTransforms()
        {
            bool result = true;
            modelRoot = part.gameObject.transform.Find("model").gameObject;

            if (modelRoot == null)
            {
                this.LogError("Root transform could not be found!");
                return false;
            }

            if (wallData != null)
            {
                try
                {
                    wallObjectPrefab = wallData.Resolve(modelRoot);
                }
                catch (ModelData.ResolveException ex)
                {
                    this.LogException(ex);
                    result = false;
                }
            }

            if (coneData == null)
            {
                this.LogError("coneData is null, cannot find transform!");
                result = false;
            }
            else
            {
                try
                {
                    coneObjectPrefab = coneData.Resolve(modelRoot);
                }
                catch (ModelData.ResolveException ex)
                {
                    this.LogException(ex);
                    result = false;
                }
            }

            return result;
        }

        private void SetupForIcon()
        {
            coneObjectPrefab.gameObject.transform.localPosition = (SegmentOffset + coneObjectPrefab.rootOffset) / scale;
            if (wallObjectPrefab != null) wallObjectPrefab.gameObject.transform.localPosition = wallObjectPrefab.rootOffset / scale;
        }

        private void FindCargoBay()
        {
            cargoBay = part.Modules.OfType<ModuleCargoBay>().Where(module => module.DeployModuleIndex == part.Modules.IndexOf(this)).FirstOrDefault();
        }

        private void HidePrefabTransforms()
        {
            coneObjectPrefab.gameObject.SetActive(false);
            wallObjectPrefab?.gameObject.SetActive(false);
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

            fairingRoot = new GameObject(FAIRING_ROOT_TRANSFORM_NAME);
            fairingRoot.transform.NestToParent(modelRoot.transform);

            slices.Clear();
            for (int i = 0; i < numSlices; i++)
            {

                GameObject sliceRoot = new GameObject("FairingSlice");

                sliceRoot.transform.NestToParent(fairingRoot.transform);
                sliceRoot.transform.localRotation = Quaternion.AngleAxis(360f / numSlices * i, axis);

                slices.Add(new FairingSlice(sliceRoot, coneObjectPrefab, wallObjectPrefab, SegmentOffset, scale));
            }

            UpdateSegments();
            UpdateTransparency();
            UpdateOpen();
            UpdateCargoBay();
            part.ModifyCoM();
        }

        private void SetupEditorGui()
        {
            if (wallData == null || maxSegments == 0) Fields[nameof(numSegments)].guiActiveEditor = false;
            UI_FloatRange numSegmentsControl = this.GetUIControl<UI_FloatRange>(nameof(numSegments));
            numSegmentsControl.onFieldChanged = OnSegmentNumberChange;
            numSegmentsControl.maxValue = Math.Max(maxSegments, numSegments);
            this.GetUIControl(nameof(transparentEditor)).onFieldChanged = OnToggleTransparent;
            this.GetUIControl(nameof(openFairing)).onFieldChanged = OnToggleOpen;
            this.GetUIControl(nameof(autoDeploy)).onFieldChanged = OnToggleAutodeploy;
        }

        private void CalculateAutodeployAltitude()
        {
            UI_FloatRange deployAltitudeControl = this.GetUIControl<UI_FloatRange>(nameof(deployAltitude));

            float newDeployAltitude;

            if (Planetarium.fetch?.Home is CelestialBody home)
            {
                newDeployAltitude = Mathf.Round((float)home.atmosphereDepth * 0.75f / 1000f / 5f) * 5f;
                deployAltitudeControl.maxValue = (float)home.atmosphereDepth / 1000f;
            }
            else
            {
                this.LogError($"[{part.name} {GetType().Name}] Cannot find home celestial body to set altitude from");
                autoDeploy = false;
                newDeployAltitude = 100f;
                deployAltitudeControl.maxValue = 200f;
            }

            if (float.IsNaN(deployAltitude)) deployAltitude = newDeployAltitude;
        }

        private void IgnoreColliders()
        {
            CollisionManager.IgnoreCollidersOnVessel(vessel, fairingRoot.GetComponentsInChildren<Collider>());
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

        private void UpdateOpen()
        {
            if (openFairing && HighLogic.LoadedSceneIsEditor)
            {
                slices.ForEach(slice => slice.SetOffset(editorOpenOffset));
            }
            else
            {
                slices.ForEach(slice => slice.SetOffset(Vector3.zero));
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

            foreach (FairingSlice slice in slices)
            {
                GameObject gameObject = slice.SliceRootObject;
                physicalObject physObj = physicalObject.ConvertToPhysicalObject(part, gameObject);
                Rigidbody rigidBody = physObj.rb;

                rigidBody.useGravity = true;
                rigidBody.mass = slice.Mass;
                rigidBody.centerOfMass = slice.CalculateCoM();
                rigidBody.drag = part.Rigidbody.drag / numSlices;
                rigidBody.angularDrag = part.Rigidbody.angularDrag;
                rigidBody.angularVelocity = part.Rigidbody.angularVelocity;
                rigidBody.maxAngularVelocity = PhysicsGlobals.MaxAngularVelocity;
                rigidBody.velocity = part.Rigidbody.velocity + Vector3.Cross(part.Rigidbody.worldCenterOfMass - vessel.CurrentCoM, vessel.angularVelocity);

                Vector3 planeNormal = part.partTransform.TransformDirection(axis);
                Vector3 centerOfMassDirection = (rigidBody.worldCenterOfMass - part.Rigidbody.worldCenterOfMass).normalized;
                Vector3 outDirection = Vector3.ProjectOnPlane(centerOfMassDirection, planeNormal).normalized;

                Vector3 forceDirection = (planeNormal * 0.5f + outDirection).normalized;
                Vector3 torqueDirection = Vector3.Cross(planeNormal, outDirection);

                rigidBody.AddForce(forceDirection * deploySpeed, ForceMode.VelocityChange);
                rigidBody.AddTorque(torqueDirection * deployAngularSpeed, ForceMode.VelocityChange);
            }

            slices.Clear();

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
