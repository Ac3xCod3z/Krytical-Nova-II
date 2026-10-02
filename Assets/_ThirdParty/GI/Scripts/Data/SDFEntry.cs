using System;
using UnityEngine;

namespace GI
{
    public class SDFEntry : ScriptableObject
    {
        public int Version;
        public int AllocationCount;
        public string ID;
        public Vector3 Scale = Vector3.one;
        public Fit Fit;
        public bool HasBuiltInfo;
        public int BuiltResolution;
        public bool BuiltForceDoubleSided;
        public byte[] SDF;
        public byte[] Voxels;
        public uint[] Allocation; // sequential index or INVALID_ID

        public int VirtualBrickCount
        {
            get
            {
                return Fit.Bricks.x * Fit.Bricks.y * Fit.Bricks.z;
            }
        }

        public int ActualBrickCount
        {
            get
            {
                if (Version <= 2)
                    return VirtualBrickCount;
                else
                    return AllocationCount;
            }
        }

        public void Overwrite(SDFEntry other)
        {
            Version = other.Version;
            AllocationCount = other.AllocationCount;
            ID = other.ID;
            Scale = other.Scale;
            Fit = other.Fit;
            SDF = other.SDF;
            Voxels = other.Voxels;
            Allocation = other.Allocation;
            HasBuiltInfo = other.HasBuiltInfo;
            BuiltResolution = other.BuiltResolution;
            BuiltForceDoubleSided = other.BuiltForceDoubleSided;
        }
    }
}