using System;
using System.Collections.Generic;
using PlaneFX.DebugUtils;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace PlaneFX
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_CubeGrid), false)]
    public class MainGridLogic : MyGameLogicComponent
    {
        /// <summary>
        /// Minimum unit wing lift required to create a lift particle
        /// </summary>
        private const int MinParticleThreshold = 10000;
        /// <summary>
        /// Particle size equals 1 at MinParticleThreshold + this value
        /// </summary>
        private const int ParticleScalar = 30000;
        /// <summary>
        /// Multiplier for the range (+/-) of supersonic to display particles.
        /// </summary>
        private const float TransonicRange = 0.05f;

        private MyCubeGrid _grid;
        private HashSet<IMyCubeBlock> _wingBlocks = new HashSet<IMyCubeBlock>();
        private Dictionary<IMyCubeBlock, MyParticleEffect> _particleBuffer = new Dictionary<IMyCubeBlock, MyParticleEffect>();
        private MyParticleEffect _transonicParticleBuffer = null;

        #region Base Methods

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            _grid = (MyCubeGrid) Entity;
            NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override void UpdateOnceBeforeFrame()
        {
            if (_grid?.Physics == null)
                return;

            _grid.OnFatBlockAdded += OnBlockAdd;
            _grid.OnFatBlockRemoved += OnBlockRemove;

            foreach (var block in _grid.GetFatBlocks())
                OnBlockAdd(block);

            NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;
        }

        public override void UpdateAfterSimulation()
        {
            if (_wingBlocks.Count == 0 || _grid.IsStatic)
                return;
            try
            {
                MyAPIGateway.Utilities.ShowNotification("Wing Count: " + _wingBlocks.Count, 1000 / 60);

                Vector3D gridPosition = _grid.PositionComp.GetPosition();
                Vector3 localVelocity = WorldToLocal(_grid.LinearVelocity + gridPosition, _grid.WorldMatrix);
                float speed = localVelocity.Length();
                if (speed < 1)
                    return;

                Vector3D dragNormal = -Vector3D.Normalize(localVelocity);

                float airDensity = MainSession.I.GetAtmosphereDensity(_grid);
                if (airDensity == 0)
                    return;

                /* Calculate transonic vapor cone */

                // Approximate curve based on https://www.engineeringtoolbox.com/elevation-speed-sound-air-d_1534.html, accurate down to 22.68 kPa.
                double speedOfSound = Math.Pow(8947200*airDensity - 899699, 1/3.42938) + 236.712;
                if (speedOfSound < 295.1)
                    speedOfSound = 295.1;

                // Check if plane is in transonic range
                if (speed < speedOfSound * (1 + TransonicRange) && speed > speedOfSound * (1 - TransonicRange))
                {
                    if (_transonicParticleBuffer == null)
                    {
                        MatrixD gridCenter = MatrixD.CreateWorld(_grid.Physics.CenterOfMassLocal);
                        MyParticlesManager.TryCreateParticleEffect("AryxAvia_WingCloudEffect", ref gridCenter, ref Vector3D.Zero, _grid.Render.GetRenderObjectID(), out _transonicParticleBuffer);
                    }

                    float velocityScalar = 1 - (float) (Math.Abs(speed - speedOfSound) / (speedOfSound * TransonicRange));
                    _transonicParticleBuffer.UserScale = velocityScalar * (((_grid.Max - _grid.Min).Length() + 1) * _grid.GridSize / 4);
                }
                else if (_transonicParticleBuffer != null)
                {
                    _transonicParticleBuffer.Stop();
                    _transonicParticleBuffer = null;
                }
                DebugDraw.AddPoint(_grid.Physics.CenterOfMassWorld, Color.Blue, -1);


                /* Calculate wing vapor */

                foreach (var block in _wingBlocks)
                {
                    // Velocity at the center of the wing block
                    // We're performing calculations relative to the grid because it's easier that way
                    WingData data = MainSession.I.WingDatas[block.BlockDefinition];
                    Vector3D wingNormal = data.Normal;
                    if (wingNormal == Vector3D.Zero)
                        continue;

                    Vector3D liftNormal = LocalToWorld(wingNormal, block.LocalMatrix) - block.LocalMatrix.Translation;

                    // angle between chord line and airflow
                    double angleOfAttack = Math.Asin(Vector3D.Dot(dragNormal, liftNormal));
                    double liftCoefficient =
                        1.35 * Math.Sin(5.75 * angleOfAttack); // Approximation of NACA 0012 airfoil

                    double dynamicUnitPressure = 0.5 * speed * speed * airDensity;

                    double liftForce = liftCoefficient * dynamicUnitPressure;

                    //DebugDraw.DrawLineZT(block.WorldMatrix.Translation,
                    //    Vector3D.Transform(wingNormal * liftForce/1000, block.WorldMatrix), Math.Abs(liftForce) > MinParticleThreshold ? Color.Blue : Color.Red, 0.25f);

                    AssignParticleEffects(block, data, (Math.Abs(liftForce) - MinParticleThreshold) / ParticleScalar);
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole(ex.ToString());
                MyAPIGateway.Utilities.ShowMessage("PlaneFX", ex.ToString());
            }
        }

        #endregion

        #region Callbacks

        private void OnBlockAdd(IMyCubeBlock block)
        {
            if (block?.GameLogic?.Container == null)
                return;

            bool hasWingLogic = false;
            foreach (var logic in block.GameLogic.Container)
            {
                if (!logic.GetType().Name.Contains("Wing")) continue;
                hasWingLogic = true;
                break;
            }

            if (!hasWingLogic)
                return;
            MyAPIGateway.Utilities.ShowNotification("Block has wing logic!");

            if (!MainSession.I.WingDatas.ContainsKey(block.BlockDefinition))
                MainSession.I.WingDatas[block.BlockDefinition] = new WingData(block);

            _wingBlocks.Add(block);
        }

        private void OnBlockRemove(IMyCubeBlock block)
        {
            _wingBlocks.Remove(block);
        }

        #endregion

        private void AssignParticleEffects(IMyCubeBlock block, WingData wingData, double intensity)
        {
            if (intensity <= 0 && _particleBuffer.ContainsKey(block))
            {
                _particleBuffer[block].Stop();
                _particleBuffer.Remove(block);
                return;
            }

            MyParticleEffect particle;

            if (!_particleBuffer.ContainsKey(block))
            {
                MyParticlesManager.TryCreateParticleEffect("AryxAvia_WingCloudEffect", ref MatrixD.Identity, ref Vector3D.Zero, block.Render.GetRenderObjectID(), out particle);

                if (particle == null)
                    return;

                _particleBuffer.Add(block, particle);
            }
            else
            {
                particle = _particleBuffer[block];
            }

            particle.Velocity = -WorldToLocal(block.CubeGrid.LinearVelocity + block.GetPosition(), block.WorldMatrix);
            particle.UserScale = (float) intensity * wingData.Volume/25;
        }

        Vector3D WorldToLocal(Vector3D pos, MatrixD parentMatrix)
        {
            MatrixD inv = MatrixD.Invert(parentMatrix);
            return Vector3D.Rotate(pos - parentMatrix.Translation, inv);
        }
        Vector3D LocalToWorld(Vector3D pos, MatrixD parentMatrix)
        {
            return Vector3D.Rotate(pos, parentMatrix) + parentMatrix.Translation;
        }
    }
}
