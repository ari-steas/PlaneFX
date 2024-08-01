using System;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace PlaneFX
{
    public class WingData
    {
        public string SubtypeId;
        public Vector3D Normal;
        public Vector3I Size;
        public float Volume;

        public WingData(IMyCubeBlock block)
        {
            SubtypeId = block.BlockDefinition.SubtypeName;
            Size = Vector3I.Abs((Vector3I) Vector3.Transform(block.Max - block.Min + Vector3I.One, block.LocalMatrix.GetOrientation())); // TODO does this need to be rotated?
            Volume = Size.X * Size.Y * Size.Z;
            CalculateNormal(block);
            //MyAPIGateway.Utilities.ShowMessage(block.BlockDefinition.SubtypeName, $"Normal: {Normal}\n    Size: {Size}");
        }

        private void CalculateNormal(IMyCubeBlock block)
        {
            // Simple solutions
            if (Size.X == 1 && Size.Y != 1 && Size.Z != 1)
                Normal = Vector3D.Right;
            else if (Size.X != 1 && Size.Y == 1 && Size.Z != 1)
                Normal = Vector3D.Up;
            else if (Size.X != 1 && Size.Y != 1 && Size.Z == 1)
                Normal = Vector3D.Forward;
            else // billions must if/else. man I hate this.
            {
                Normal = Vector3D.Zero;
            }
        }
    }
}
