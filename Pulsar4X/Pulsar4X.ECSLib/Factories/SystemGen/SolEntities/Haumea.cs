﻿using Pulsar4X.Orbital;
using System;
using System.Collections.Generic;

namespace Pulsar4X.ECSLib.Factories.SystemGen
{
    public static partial class SolEntities
    {
        /// <summary>
        /// Creates Haumea
        /// </summary>
        /// <remarks>https://en.wikipedia.org/wiki/Haumea</remarks>
        public static Entity Haumea(Game game, StarSystem sol, Entity sun, DateTime epoch, SensorProfileDB sensorProfile)
        {
            MassVolumeDB sunMVDB = sun.GetDataBlob<MassVolumeDB>();

            SystemBodyInfoDB planetBodyDB = new SystemBodyInfoDB { BodyType = BodyType.DwarfPlanet, SupportsPopulations = true };
            MassVolumeDB planetMVDB = MassVolumeDB.NewFromMassAndRadius_AU(4.006E21, Distance.KmToAU(798));
            NameDB planetNameDB = new NameDB("Haumea");

            double planetSemiMajorAxisAU = 43.182;
            double planetEccentricity = 0.19489;
            double planetEclipticInclination = 0;   // 28.214
            double planetLoAN = 122.163;
            double planetAoP = 238.778;
            double planetMeanAnomaly = 217.774;

            OrbitDB planetOrbitDB = OrbitDB.FromMajorPlanetFormat(sun, sunMVDB.MassDry, planetMVDB.MassDry, planetSemiMajorAxisAU, planetEccentricity, planetEclipticInclination, planetLoAN, planetAoP, planetMeanAnomaly, epoch);
            planetBodyDB.BaseTemperature = (float)SystemBodyFactory.CalculateBaseTemperatureOfBody(sun, planetOrbitDB);
            PositionDB planetPositionDB = new PositionDB(planetOrbitDB.GetPosition(StaticRefLib.CurrentDateTime), sol.Guid, sun);

            Entity planet = new Entity(sol, new List<BaseDataBlob> { sensorProfile, planetPositionDB, planetBodyDB, planetMVDB, planetNameDB, planetOrbitDB });
            SensorProcessorTools.PlanetEmmisionSig(sensorProfile, planetBodyDB, planetMVDB);
            return planet;
        }
    }
}
