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
        private readonly GameObject wallPrefab;
        private readonly ModelData coneData;
        private readonly ModelData wallData;
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

        public float Mass => NumSegments * (wallData?.mass ?? 0) + coneData.mass;

        #endregion

        #region Constructors

        public FairingSlice(GameObject sliceRoot, GameObject conePrefab, GameObject wallPrefab, ModelData coneData, ModelData wallData, Vector3 segmentOffset, float scale)
        {
            SliceRootObject = sliceRoot ?? throw new ArgumentNullException(nameof(sliceRoot));
            this.wallPrefab = wallPrefab;
            this.coneData = coneData ?? throw new ArgumentNullException(nameof(coneData));
            this.wallData = wallData;
            this.segmentOffset = segmentOffset;
            this.scale = scale;

            subRootObject = new GameObject("FairingSlice-Sub");
            subRootObject.transform.NestToParent(SliceRootObject.transform);

            coneObject = UnityEngine.Object.Instantiate(conePrefab, subRootObject.transform);
            coneObject.transform.localScale *= scale;
            coneObject.gameObject.SetActive(true);
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
            Vector3 CoM = (coneObject.transform.localPosition + coneData.CoM) * coneData.mass;

            foreach (GameObject wallObject in wallObjects)
            {
                CoM += (wallObject.transform.localPosition + wallData.CoM) * wallData.mass;
            }

            CoM /= Mass;

            return CoM;
        }

        public void UpdateSegments(int newNumSegments)
        {
            coneObject.transform.localPosition = (segmentOffset * newNumSegments) + coneData.rootOffset;

            if (newNumSegments != 0 && (wallData == null || wallPrefab == null))
                throw new InvalidOperationException("Cannot change segment number when wall data is null");

            int segmentChange = newNumSegments - NumSegments;

            if (segmentChange > 0)
            {
                for (int i = NumSegments; i < newNumSegments; i++)
                {
                    GameObject wallObject = UnityEngine.Object.Instantiate(wallPrefab, subRootObject.transform);
                    wallObject.SetActive(true);
                    wallObject.transform.localPosition = segmentOffset * i + wallData.rootOffset;
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
