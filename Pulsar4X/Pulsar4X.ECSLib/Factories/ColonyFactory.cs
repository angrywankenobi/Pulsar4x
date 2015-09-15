﻿using System.Collections.Generic;

namespace Pulsar4X.ECSLib
{
    public static class ColonyFactory
    {
        /// <summary>
        /// Creates a new colony with zero population.
        /// </summary>
        /// <param name="systemEntityManager"></param>
        /// <param name="factionEntity"></param>
        /// <returns></returns>
        public static Entity CreateColony(Entity factionEntity, Entity speciesEntity, Entity planetEntity)
        {
            List<BaseDataBlob> blobs = new List<BaseDataBlob>();
            string planetName = planetEntity.GetDataBlob<NameDB>().GetName(factionEntity);
            NameDB name = new NameDB(planetName + " Colony"); // TODO: Review default name.
            blobs.Add(name);
            ColonyInfoDB colonyInfoDB = new ColonyInfoDB(Entity.InvalidEntity, 0, planetEntity);
            blobs.Add(colonyInfoDB);
            ColonyMinesDB colonyMinesDB = new ColonyMinesDB();
            InstallationsDB colonyInstallationsDB = new InstallationsDB();
            blobs.Add(colonyInstallationsDB);
            blobs.Add(colonyMinesDB);
            Entity colonyEntity = new Entity(planetEntity.Manager, blobs);
            factionEntity.GetDataBlob<FactionDB>().Colonies.Add(colonyEntity);
            return colonyEntity;
        }
    }
}