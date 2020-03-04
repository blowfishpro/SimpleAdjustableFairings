using System;
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

        private readonly GameObject subRootObject;
        private readonly GameObject coneObject;
        private readonly ResolvedModelData conePrefab;
        private readonly ResolvedModelData capPrefab;
        private readonly ResolvedModelData wallPrefab;
        private readonly ResolvedModelData wallBasePrefab;
        private readonly Vector3 segmentOffset;
        private readonly float scale;

        private readonly List<GameObject> wallObjects = new List<GameObject>();

        private bool transparent = false;

        #endregion

        #region Properties

        public GameObject SliceRootObject { get; }

        public int NumSegments
        {
            get
            {
                return wallObjects.Count;
            }
            set
            {
                UpdateSegments(value);
            }
        }

        public float Mass => (wallBasePrefab?.mass ?? 0) + (NumSegments * (wallPrefab?.mass ?? 0)) + conePrefab.mass + (capPrefab?.mass ?? 0);

        #endregion

        #region Constructors

        public FairingSlice(GameObject sliceRoot, ResolvedModelData conePrefab, ResolvedModelData capPrefab, ResolvedModelData wallPrefab, ResolvedModelData wallBasePrefab, Vector3 segmentOffset, float scale)
        {
            SliceRootObject = sliceRoot ?? throw new ArgumentNullException(nameof(sliceRoot));
            this.conePrefab = conePrefab ?? throw new ArgumentNullException(nameof(conePrefab));
            this.capPrefab = capPrefab;
            this.wallPrefab = wallPrefab;
            this.wallBasePrefab = wallBasePrefab;
            this.segmentOffset = segmentOffset;
            this.scale = scale;

            subRootObject = new GameObject("FairingSlice-Sub");
            subRootObject.transform.NestToParent(SliceRootObject.transform);

            coneObject = new GameObject("FairingCone");
            coneObject.transform.NestToParent(subRootObject.transform);
            GameObject mainConeObject = UnityEngine.Object.Instantiate(conePrefab.gameObject, coneObject.transform);
            mainConeObject.transform.localPosition = conePrefab.rootOffset;
            mainConeObject.transform.localScale *= scale;
            mainConeObject.gameObject.SetActive(true);

            if (capPrefab != null)
            {
                GameObject coneCapObject = UnityEngine.Object.Instantiate(capPrefab.gameObject, coneObject.transform);
                coneCapObject.transform.localPosition = capPrefab.rootOffset;
                coneCapObject.transform.localScale *= scale;
                coneCapObject.gameObject.SetActive(true);
            }

            if (wallBasePrefab != null)
            {
                GameObject wallBaseObject = UnityEngine.Object.Instantiate(wallBasePrefab.gameObject, subRootObject.transform);
                wallBaseObject.transform.localPosition = wallBasePrefab.rootOffset;
                wallBaseObject.transform.localScale *= scale;
                wallBaseObject.gameObject.SetActive(true);
            }
        }

        #endregion

        #region Public Methods

        public void SetOffset(Vector3 offset)
        {
            subRootObject.transform.localPosition = offset;
        }

        public void MakeTransparent()
        {
            coneObject.MakeTransparent();
            wallObjects.ForEach(transform => transform.MakeTransparent());

            transparent = true;
        }

        public void MakeOpaque()
        {
            coneObject.MakeOpaque();
            wallObjects.ForEach(transform => transform.MakeOpaque());

            transparent = false;
        }

        public Vector3 CalculateCoM()
        {
            Vector3 CoM = (coneObject.transform.localPosition + conePrefab.CoM) * conePrefab.mass;

            if (capPrefab != null)
                CoM += (coneObject.transform.localPosition + capPrefab.CoM) * capPrefab.mass;

            foreach (GameObject wallObject in wallObjects)
            {
                CoM += (wallObject.transform.localPosition + wallPrefab.CoM) * wallPrefab.mass;
            }

            if (wallBasePrefab != null)
                CoM += wallBasePrefab.CoM * wallBasePrefab.mass;

            CoM /= Mass;

            return CoM;
        }

        public void UpdateSegments(int newNumSegments)
        {
            coneObject.transform.localPosition = segmentOffset * newNumSegments;

            if (newNumSegments != 0 && wallPrefab == null)
                throw new InvalidOperationException("Cannot change segment number when wall data is null");

            int segmentChange = newNumSegments - NumSegments;

            if (segmentChange > 0)
            {
                for (int i = NumSegments; i < newNumSegments; i++)
                {
                    GameObject wallObject = UnityEngine.Object.Instantiate(wallPrefab.gameObject, subRootObject.transform);
                    wallObject.SetActive(true);
                    wallObject.transform.localPosition = segmentOffset * i + wallPrefab.rootOffset;
                    wallObject.transform.localScale *= scale;
                    wallObjects.Add(wallObject);

                    if (transparent) wallObject.MakeTransparent();
                }
            }
            else if (segmentChange < 0)
            {
                for (int i = NumSegments - 1; i >= newNumSegments; i--)
                {
                    UnityEngine.Object.Destroy(wallObjects[i]);
                    wallObjects.RemoveAt(i);
                }
            }
        }

        public void UpdateCollidersEnabled(bool enabled)
        {
            coneObject.SetCollidersEnabled(enabled);
            wallObjects.ForEach(transform => transform.SetCollidersEnabled(enabled));
        }

        #endregion
    }
}
