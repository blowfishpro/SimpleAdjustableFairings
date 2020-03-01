using System.Collections.Generic;
using UnityEngine;

namespace SimpleAdjustableFairings
{
    public class FairingSlice
    {
        #region Constants

        public const string OBJECT_NAME = "FairingSlice";

        #endregion

        #region Private Fields

        private readonly ModuleSimpleAdjustableFairing parent;

        private readonly Transform rootTransform;
        private readonly Transform coneTransform;
        private readonly List<Transform> wallTransforms = new List<Transform>();

        private Quaternion rotation;

        private bool detached = false;

        #endregion

        #region Properties

        public int NumSegments
        {
            get
            {
                return wallTransforms.Count;
            }
            set
            {
                UpdateSegments(value);
            }
        }

        public float Mass => NumSegments * WallData.mass + ConeData.mass;

        private ModelData WallData => parent.WallData;
        private ModelData ConeData => parent.ConeData;

        #endregion

        #region Constructors

        public FairingSlice(ModuleSimpleAdjustableFairing parent, int index)
        {
            this.parent = parent;

            GameObject gameObject = new GameObject(OBJECT_NAME);
            rootTransform = gameObject.transform;

            rotation = Quaternion.AngleAxis(360f / parent.numSlices * index, parent.axis);

            rootTransform.NestToParent(parent.FairingRootTransform);
            rootTransform.localRotation = rotation;

            coneTransform = Object.Instantiate(parent.PrefabConeTransform);
            coneTransform.NestToParent(rootTransform);
            coneTransform.gameObject.SetActive(true);
            coneTransform.SetCollidersEnabled(parent.FairingCollidersEnabled);
            coneTransform.localScale *= parent.scale;
        }

        #endregion

        #region Public Methods

        public void Open()
        {
            rootTransform.localPosition = rotation * parent.editorOpenOffset;
        }

        public void Close()
        {
            rootTransform.localPosition = Vector3.zero;
        }

        public void MakeTransparent()
        {
            coneTransform.MakeTransparent();
            wallTransforms.ForEach(transform => transform.MakeTransparent());

            UpdateCollidersEnabled();
        }

        public void MakeOpaque()
        {
            coneTransform.MakeOpaque();
            wallTransforms.ForEach(transform => transform.MakeOpaque());

            UpdateCollidersEnabled();
        }

        public void Detach()
        {
            if (detached) return;

            physicalObject physObj = physicalObject.ConvertToPhysicalObject(parent.part, rootTransform.gameObject);
            Rigidbody rigidBody = physObj.rb;

            rigidBody.useGravity = true;
            rigidBody.mass = Mass;
            rigidBody.centerOfMass = CalculateCoM();
            rigidBody.drag = parent.part.Rigidbody.drag / parent.numSlices;
            rigidBody.angularDrag = parent.part.Rigidbody.angularDrag;
            rigidBody.angularVelocity = parent.part.Rigidbody.angularVelocity;
            rigidBody.maxAngularVelocity = PhysicsGlobals.MaxAngularVelocity;
            rigidBody.velocity = parent.part.Rigidbody.velocity + Vector3.Cross(parent.part.Rigidbody.worldCenterOfMass - parent.vessel.CurrentCoM, parent.vessel.angularVelocity);

            Vector3 planeNormal = parent.part.partTransform.TransformDirection(parent.axis);
            Vector3 centerOfMassDirection = (rigidBody.worldCenterOfMass - parent.part.Rigidbody.worldCenterOfMass).normalized;
            Vector3 outDirection = Vector3.ProjectOnPlane(centerOfMassDirection, planeNormal).normalized;

            Vector3 forceDirection = (planeNormal * 0.5f + outDirection).normalized;
            Vector3 torqueDirection = Vector3.Cross(planeNormal, outDirection);

            rigidBody.AddForce(forceDirection * parent.deploySpeed, ForceMode.VelocityChange);
            rigidBody.AddTorque(torqueDirection * parent.deployAngularSpeed, ForceMode.VelocityChange);

            detached = true;
        }

        public Vector3 CalculateCoM()
        {
            Vector3 CoM = (coneTransform.localPosition + ConeData.CoM) * ConeData.mass;

            foreach (Transform transform in wallTransforms)
            {
                CoM += (transform.localPosition + WallData.CoM) * WallData.mass;
            }

            CoM /= Mass;

            return CoM;
        }

        public Vector3 CalculatePartRelativeCoM()
        {
            Vector3 worldCoM = rootTransform.TransformPoint(CalculateCoM());
            return parent.part.partTransform.InverseTransformPoint(worldCoM);
        }

        #endregion

        #region Private Methods

        private void UpdateSegments(int newNumSegments)
        {
            coneTransform.localPosition = parent.SegmentOffset * newNumSegments + ConeData.rootOffset;

            int segmentChange = newNumSegments - NumSegments;

            if (segmentChange > 0)
            {
                for (int i = NumSegments; i < newNumSegments; i++)
                {
                    Transform wallTransform = Object.Instantiate(parent.PrefabWallTransform);
                    wallTransform.NestToParent(rootTransform);
                    wallTransform.gameObject.SetActive(true);
                    wallTransform.localPosition = parent.SegmentOffset * i + WallData.rootOffset;
                    wallTransform.localScale *= parent.scale;

                    if (parent.transparentEditor)
                        wallTransform.MakeTransparent();

                    wallTransform.SetCollidersEnabled(parent.FairingCollidersEnabled);
                    wallTransforms.Add(wallTransform);
                }
            }
            else if (segmentChange < 0)
            {
                for (int i = NumSegments - 1; i >= newNumSegments; i--)
                {
                    Object.Destroy(wallTransforms[i].gameObject);
                    wallTransforms.RemoveAt(i);
                }
            }
        }

        private void UpdateCollidersEnabled()
        {
            coneTransform.SetCollidersEnabled(parent.FairingCollidersEnabled);
            wallTransforms.ForEach(transform => transform.SetCollidersEnabled(parent.FairingCollidersEnabled));
        }

        #endregion
    }
}
