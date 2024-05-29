using System;
using Sandbox.Definitions;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace PlaneFX
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class MainSession : MySessionComponentBase
    {
        public static MainSession I;
        public Dictionary<SerializableDefinitionId, WingData> WingDatas = new Dictionary<SerializableDefinitionId, WingData>();

        private readonly List<MyPlanet> _planets = new List<MyPlanet>();

        #region Base Methods

        public override void LoadData()
        {
            I = this;
            MyAPIGateway.Entities.OnEntityAdd += OnEntityAdd;
        }

        protected override void UnloadData()
        {
            I = null;
        }

        #endregion

        public float GetAtmosphereDensity(IMyCubeGrid grid)
        {
            Vector3D gridPos = grid.PositionComp.GetPosition();
            foreach (var planet in _planets.ToArray())
            {
                if (planet.Closed || planet.MarkedForClose)
                {
                    _planets.Remove(planet);
                    continue;
                }

                if (Vector3D.DistanceSquared(gridPos, planet.PositionComp.GetPosition()) >
                    planet.AtmosphereRadius * planet.AtmosphereRadius)
                    continue;
                return planet.GetAirDensity(gridPos);
            }

            return 0;
        }

        private void OnEntityAdd(IMyEntity entity)
        {
            var planet = entity as MyPlanet;
            if (planet != null)
                _planets.Add(planet);
        }
    }
}
