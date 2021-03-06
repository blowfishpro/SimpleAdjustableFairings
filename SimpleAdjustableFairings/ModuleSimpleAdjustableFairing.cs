﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using UnityEngine;

namespace SimpleAdjustableFairings
{
    public class ModuleSimpleAdjustableFairing : PartModule, IScalarModule, IPartCoMModifier, ISerializationCallbackReceiver, IMultipleDragCube
    {
        public const string FAIRING_ROOT_TRANSFORM_NAME = "FairingRoot";

        private const string DRAG_CUBE_NAME_CLOSED = "Closed";
        private const string DRAG_CUBE_NAME_DEPLOYED = "Deployed";

        private static readonly string[] DRAG_CUBE_NAMES = { DRAG_CUBE_NAME_CLOSED, DRAG_CUBE_NAME_DEPLOYED };

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

        [KSPField]
        public string uiGroupName = "fairing";

        [KSPField]
        public string uiGroupDisplayName = "Fairing";

        [Persistent(name = "WALL_BASE")]
        public ModelData wallBaseData = null;

        [Persistent(name = "WALL")]
        public ModelData wallData = null;

        [Persistent(name = "CONE")]
        public ModelData coneData = null;

        [Persistent(name = "CAP")]
        public ModelData capData = null;

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

        private ResolvedModelData coneObjectPrefab;
        private ResolvedModelData capObjectPrefab;
        private ResolvedModelData wallObjectPrefab;
        private ResolvedModelData wallBaseObjectPrefab;

        private GameObject modelRoot;
        private GameObject fairingRoot;

        private readonly List<FairingSlice> slices = new List<FairingSlice>();
        private ModuleCargoBay cargoBay;

        [SerializeField]
        private string serializedData;

        private bool needsRecalculateDragCubes;
        private bool needsNotifyFARToRevoxelize;

		private float fairingScale = 1.0f;

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

        [KSPEvent]
        public void ModuleDataChanged(BaseEventDetails details)
        {
            if (!FindTransforms()) return;

            // Not yet initialized
            if (fairingRoot == null) return;

            SetupFairing();

            if (details?.Get<Action>("requestNotifyFARToRevoxelize") is Action requestNotifyFarToRevoxelize)
                requestNotifyFarToRevoxelize();
            else
                NotifyFARToRevoxelize();


            if (details?.Get<Action>("requestRecalculateDragCubes") is Action requestRecalculateDragCubes)
                requestRecalculateDragCubes();
            else
                RecalculateDragCubes();

            IgnoreColliders();
        }

        [KSPEvent]
        public void DragCubesWereRecalculated() => needsRecalculateDragCubes = false;

        [KSPEvent]
        public void FarWasNotifiedToRevoxelize() => needsNotifyFARToRevoxelize = false;

        [KSPEvent]
        void OnPartScaleChanged(BaseEventDetails data)
        {
            fairingScale = data.Get<float>("factorAbsolute");

            if (!FindTransforms()) return;

            // Not yet initialized
            if (fairingRoot == null) return;

            SetupFairing();

            NotifyFARToRevoxelize();
            RecalculateDragCubes();

            IgnoreColliders();
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

            ConfigNode.LoadObjectFromConfig(this, node);
        }

        public override void OnIconCreate()
        {
            base.OnIconCreate();

            if (FindTransforms())
                SetupForIcon();
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            if (!FindTransforms()) return;

            if (state == StartState.Editor)
            {
                coneObjectPrefab.gameObject.SetCollidersEnabled(false);
                capObjectPrefab?.gameObject.SetCollidersEnabled(false);
                wallObjectPrefab?.gameObject.SetCollidersEnabled(false);
                wallBaseObjectPrefab?.gameObject.SetCollidersEnabled(false);
            }

            FindCargoBay();

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

            SetUiGroups();
            UpdateDeployAltitudeVisibility();

            if (state == StartState.Editor)
            {
                HidePrefabTransforms();
                SetupEditorGui();
            }
            else
            {
                DestroyPrefabTransforms();
            }
        }

        public override void OnStartFinished(StartState state)
        {
            base.OnStartFinished(state);

            if (deployed)
            {
                part.DragCubes.SetCubeWeight(DRAG_CUBE_NAME_CLOSED, 0);
                part.DragCubes.SetCubeWeight(DRAG_CUBE_NAME_DEPLOYED, 1);
            }
            else
            {
                part.DragCubes.SetCubeWeight(DRAG_CUBE_NAME_CLOSED, 1);
                part.DragCubes.SetCubeWeight(DRAG_CUBE_NAME_DEPLOYED, 0);

                NotifyFARToRevoxelize();
                RecalculateDragCubes();
                IgnoreColliders();
            }
        }

        #endregion

        #region Serialization

        public void OnBeforeSerialize()
        {
            ConfigNode node = new ConfigNode("SERIALIZED_DATA");
            ConfigNode.CreateConfigFromObject(this, node);
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


            ConfigNode.LoadObjectFromConfig(this, node2);
        }

        #endregion

        #region Callbacks

        private void SetupFairingOnAttach()
        {
            part.OnEditorAttach -= SetupFairingOnAttach;
            SetupFairing();
            NotifyFARToRevoxelize();
            RecalculateDragCubes();
        }

        private void OnSegmentNumberChange(BaseField field, object oldValue)
        {
            UpdateSegments();
            NotifyModelUpdate();
            UpdateCargoBay();
            part.ModifyCoM();
            part.RefreshHighlighter();
            NotifyFARToRevoxelize();
            RecalculateDragCubes();
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

        public bool IsMultipleCubesActive => true;

        public string[] GetDragCubeNames() => DRAG_CUBE_NAMES;

        public void AssumeDragCubePosition(string name)
        {
            if (fairingRoot == null) return;

            if (name == DRAG_CUBE_NAME_CLOSED)
                fairingRoot.SetActive(true);
            else if (name == DRAG_CUBE_NAME_DEPLOYED)
                fairingRoot.SetActive(false);
            else
                throw new ArgumentException($"Invalid drag cube name: {name}");
        }

        public bool UsesProceduralDragCubes() => false;

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

            if (wallBaseData != null)
            {
                try
                {
                    wallBaseObjectPrefab = wallBaseData.Resolve(modelRoot, fairingScale);
                }
                catch (ModelData.ResolveException ex)
                {
                    this.LogException(ex);
                    result = false;
                }
            }

            if (wallData != null)
            {
                try
                {
                    wallObjectPrefab = wallData.Resolve(modelRoot, fairingScale);
                }
                catch (ModelData.ResolveException ex)
                {
                    this.LogException(ex);
                    result = false;
                }
            }

            if (capData != null)
            {
                try
                {
                    capObjectPrefab = capData.Resolve(modelRoot, fairingScale);
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
            else if (coneData.enabled == false)
            {
                this.LogError("coneData has enabled = false, this is illegal!");
                result = false;
            }
            else
            {
                try
                {
                    coneObjectPrefab = coneData.Resolve(modelRoot, fairingScale);
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
            if (capObjectPrefab != null) capObjectPrefab.gameObject.transform.localPosition = (SegmentOffset + capObjectPrefab.rootOffset) / scale;
            if (wallObjectPrefab != null) wallObjectPrefab.gameObject.transform.localPosition = wallObjectPrefab.rootOffset / scale;
            if (wallBaseObjectPrefab != null) wallBaseObjectPrefab.gameObject.transform.localPosition = wallBaseObjectPrefab.rootOffset / scale;
        }

        private void FindCargoBay()
        {
            cargoBay = part.Modules.OfType<ModuleCargoBay>().Where(module => module.DeployModuleIndex == part.Modules.IndexOf(this)).FirstOrDefault();
        }

        private void HidePrefabTransforms()
        {
            coneObjectPrefab.gameObject.SetActive(false);
            capObjectPrefab?.gameObject.SetActive(false);
            wallObjectPrefab?.gameObject.SetActive(false);
            wallBaseObjectPrefab?.gameObject.SetActive(false);
        }

        private void DestroyPrefabTransforms()
        {
            Destroy(coneObjectPrefab.gameObject);
            if (capObjectPrefab != null) Destroy(capObjectPrefab.gameObject);
            if (wallObjectPrefab != null) Destroy(wallObjectPrefab.gameObject);
            if (wallBaseObjectPrefab != null) Destroy(wallBaseObjectPrefab.gameObject);
        }

        private void HideDeployEvent()
        {
            Events[nameof(DeployEvent)].guiActive = false;
            Fields[nameof(autoDeploy)].guiActive = false;
            Fields[nameof(deployAltitude)].guiActive = false;
        }

        private void SetupFairing()
        {
            // If we are duplicating in the editor or reinitializing, there will be some leftovers
            // Easier to just get rid of them rather than try to rebuild the hierarchy
            GameObject oldFairing = part.FindModelTransform(FAIRING_ROOT_TRANSFORM_NAME)?.gameObject;
            if (oldFairing != null)
            {
                oldFairing.transform.parent = null;
                Destroy(oldFairing);
            }

            fairingRoot = new GameObject(FAIRING_ROOT_TRANSFORM_NAME);
            fairingRoot.transform.NestToParent(modelRoot.transform);

            ResolvedModelData currentCapObjectPrefab = capObjectPrefab;

            slices.Clear();
            for (int i = 0; i < numSlices; i++)
            {
                GameObject sliceRoot = new GameObject("FairingSlice");

                sliceRoot.transform.NestToParent(fairingRoot.transform);
                sliceRoot.transform.localRotation = Quaternion.AngleAxis(360f / numSlices * i, axis);

                slices.Add(new FairingSlice(sliceRoot, coneObjectPrefab, currentCapObjectPrefab, wallObjectPrefab, wallBaseObjectPrefab, SegmentOffset, scale));

                currentCapObjectPrefab = null;
            }

            UpdateSegments();
            UpdateTransparency();
            NotifyModelUpdate();
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

        private void SetUiGroups()
        {
            foreach (BaseField field in Fields)
            {
                field.group.name = uiGroupName;
                field.group.displayName = uiGroupDisplayName;
            }

            foreach (BaseEvent baseEvent in Events)
            {
                baseEvent.group.name = uiGroupName;
                baseEvent.group.displayName = uiGroupDisplayName;
            }
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
            if (!HighLogic.LoadedSceneIsFlight) return;

            CollisionManager.IgnoreCollidersOnVessel(vessel, fairingRoot.GetComponentsInChildren<Collider>());
        }

        private void UpdateSegments()
        {
            slices.ForEach(slice => slice.NumSegments = (int)numSegments);
            needsNotifyFARToRevoxelize = true;
            needsRecalculateDragCubes = true;
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

        private void NotifyFARToRevoxelize()
        {
            if (!needsNotifyFARToRevoxelize) return;

            part.SendMessage("GeometryPartModuleRebuildMeshData");
            part.SendMessage(nameof(FarWasNotifiedToRevoxelize));
            needsNotifyFARToRevoxelize = false;
        }

        private void RecalculateDragCubes()
        {
            if (!needsRecalculateDragCubes) return;

            IEnumerator RecalculateDragCubesCoroutine()
            {
                float[] dragCubeWeights = part.DragCubes.Cubes.Select(cube => cube.Weight).ToArray();
                part.DragCubes.ClearCubes();
                yield return DragCubeSystem.Instance.SetupDragCubeCoroutine(part, null);

                if (dragCubeWeights.Length == part.DragCubes.Cubes.Count)
                {
                    for (int i = 0; i < dragCubeWeights.Length; i++)
                    {
                        part.DragCubes.Cubes[i].Weight = dragCubeWeights[i];
                    }
                }
                else
                {
                    Debug.LogError($"[{GetType().Name}] Cannot reassign cube weights: had {dragCubeWeights.Length} cubes before but now have {part.DragCubes.Cubes.Count}");
                }

                part.DragCubes.ForceUpdate(weights: true, occlusion: true);
            }

            StartCoroutine(RecalculateDragCubesCoroutine());

            part.SendMessage(nameof(DragCubesWereRecalculated));
            needsRecalculateDragCubes = false;
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

            if (UIPartActionController.Instance is UIPartActionController partActionController && partActionController.GetItem(part, false) is UIPartActionWindow window) window.displayDirty = true;

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

            needsNotifyFARToRevoxelize = true;
            NotifyFARToRevoxelize();

            part.DragCubes.SetCubeWeight(DRAG_CUBE_NAME_CLOSED, 0);
            part.DragCubes.SetCubeWeight(DRAG_CUBE_NAME_DEPLOYED, 1);
            part.DragCubes.ForceUpdate(weights: true, occlusion: true);

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

        private void NotifyModelUpdate() => part.SendEvent("OnPartModelChanged");

        #endregion
    }
}
