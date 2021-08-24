using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;
using UObject = UnityEngine.Object;

namespace UnityEditor.Splines
{
    interface IEditableSplineConversionData
    {
        bool isDirty { get; set; }
        UObject conversionTarget { get; set; }
        int conversionIndex { get; set; }

        void ValidateData();
        void CopyConversionDataFrom(IEditableSplineConversionData spline);
    }

    interface IEditableSpline
    {
        bool canBeClosed { get; set; }
        bool closed { get; set; }
        int knotCount { get; }
        /// <summary> Matrix that transforms a point from local (spline) into world space. </summary>
        Matrix4x4 localToWorldMatrix { get; }
        /// <summary> Matrix that transforms a point from world space into local (spline) space. </summary>
        Matrix4x4 worldToLocalMatrix { get; }

        EditableKnot GetKnot(int index);
        bool GetPreviousKnot(int index, out EditableKnot knot);
        bool GetNextKnot(int index, out EditableKnot knot);
        void Resize(int targetKnotCount);
        EditableKnot AddKnot();
        void RemoveKnotAt(int index);
        EditableKnot InsertKnot(int index);
        float3 GetPointOnCurve(CurveData curve, float t);
        /// <summary>
        /// Given an editable knot, returns its in and out tangents in local (spline) space.
        /// </summary>
        /// <param name="knot">An editable knot.</param>
        /// <param name="localTangentIn">Knot's in tangent in local (spline) space.</param>
        /// <param name="localTangentOut">Knot's out tangent in local (spline) space.</param>
        void GetLocalTangents(EditableKnot knot, out float3 localTangentIn, out float3 localTangentOut);
        void SetDirty();
        void ToBezier(List<BezierKnot> results);
        void FromBezier(IReadOnlyList<BezierKnot> knots);
    }

    [Serializable]
    abstract class EditableSpline<T> : IEditableSpline, IEditableSplineConversionData
        where T : EditableKnot, new()
    {
        const int k_MinimumKnotCountToBeClosed = 3;

        //Serialized fields will be used for tools inspector
        [SerializeField]
        List<T> m_Knots = new List<T>();

        [SerializeField]
        bool m_Closed = false;

        UObject m_ConversionTarget;
        int m_ConversionIndex;

        bool m_IsDirty = false;

        bool m_CanBeClosed = true;

        public Matrix4x4 localToWorldMatrix => 
            m_ConversionTarget != null && m_ConversionTarget is Component component
            ? component.transform.localToWorldMatrix
            : Matrix4x4.identity;

        public Matrix4x4 worldToLocalMatrix => localToWorldMatrix.inverse;

        UObject IEditableSplineConversionData.conversionTarget
        {
            get => m_ConversionTarget;
            set => m_ConversionTarget = value;
        }

        //the index in the target array created at conversion
        int IEditableSplineConversionData.conversionIndex
        {
            get => m_ConversionIndex;
            set => m_ConversionIndex = value;
        }

        void IEditableSplineConversionData.CopyConversionDataFrom(IEditableSplineConversionData spline)
        {
            m_ConversionTarget = spline.conversionTarget;
            m_ConversionIndex = spline.conversionIndex;
        }

        void IEditableSplineConversionData.ValidateData()
        {
            UpdateKnotIndices();
            foreach (var knot in m_Knots)
            {
                knot.spline = this;
                knot.splineConversionData = this;
                knot.ValidateData();
            }
        }

        public bool canBeClosed
        {
            get => m_CanBeClosed;
            set
            {
                m_CanBeClosed = value;
                if (!m_CanBeClosed)
                {
                    m_Closed = false;
                    SetDirty();
                }
            }
        }

        public bool closed
        {
            get => knotCount >= k_MinimumKnotCountToBeClosed && m_Closed;
            set
            {
                if (m_Closed == value || !m_CanBeClosed)
                    return;

                m_Closed = value;
                SetDirty();
            }
        }

        bool IEditableSplineConversionData.isDirty
        {
            get => m_IsDirty;
            set => m_IsDirty = value;
        }

        public int knotCount => m_Knots.Count;

        EditableKnot IEditableSpline.GetKnot(int index)
        {
            return GetKnot(index);
        }

        public T GetKnot(int index)
        {
            return m_Knots[index];
        }

        public bool GetPreviousKnot(int index, out EditableKnot knot)
        {
            bool result = GetPreviousKnot(index, out T rawKnot);
            knot = rawKnot;
            return result;
        }

        public bool GetPreviousKnot(int index, out T knot)
        {
            if (knotCount > 0)
            {
                int next = index - 1;
                if (next >= 0)
                {
                    knot = m_Knots[next];
                    return true;
                }

                if (closed)
                {
                    knot = m_Knots[m_Knots.Count - 1];
                    return true;
                }
            }

            knot = null;
            return false;
        }

        public bool GetNextKnot(int index, out EditableKnot knot)
        {
            if (knotCount > 0)
            {
                int next = index + 1;
                if (next < m_Knots.Count)
                {
                    knot = m_Knots[next];
                    return true;
                }

                if (closed)
                {
                    knot = m_Knots[0];
                    return true;
                }
            }

            knot = null;
            return false;
        }

        public void Resize(int targetKnotCount)
        {
            if (knotCount > targetKnotCount)
            {
                m_Knots.RemoveRange(targetKnotCount, knotCount - targetKnotCount);
            }
            else if (knotCount < targetKnotCount)
            {
                while (knotCount < targetKnotCount)
                {
                    AddKnot();
                }
            }

            SetDirty(); 
        }

        public EditableKnot AddKnot()
        {
            var knot = CreateKnot();
            knot.index = m_Knots.Count;
            m_Knots.Add(knot);
            SetDirty();
            return knot;
        }

        public void RemoveKnotAt(int index)
        {
            EditableKnot knot = m_Knots[index];

            SplineSelection.Remove(knot);
            SplineSelection.OnKnotRemoved(this, index); 
            
            knot.index = -1;
            m_Knots.RemoveAt(index);
            UpdateKnotIndices();
            SetDirty();
        }

        public EditableKnot InsertKnot(int index)
        {
            var knot = CreateKnot();
            m_Knots.Insert(index, knot);
            UpdateKnotIndices();
            SetDirty(); 

            SplineSelection.MoveAllIndexUpFromIndexToEnd(this, index);
            return knot;
        }

        void UpdateKnotIndices()
        {
            for (int i = 0; i < m_Knots.Count; ++i)
            {
                m_Knots[i].index = i;
            } 
        }

        T CreateKnot()
        {
            return new T { spline = this, splineConversionData = this};
        }

        public void SetDirty()
        {
            m_IsDirty = true;
        }

        public abstract float3 GetPointOnCurve(CurveData curve, float t);
        public abstract void GetLocalTangents(EditableKnot knot, out float3 localTangentIn, out float3 localTangentOut);
        public abstract void ToBezier(List<BezierKnot> results);
        public abstract void FromBezier(IReadOnlyList<BezierKnot> knots);
    }
}
