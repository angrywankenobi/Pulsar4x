﻿using Pulsar4X.ECSLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Pulsar4X.Tests
{
    using NUnit.Framework;

    [TestFixture]
    [Description("Tests the SerialzationManagers Import/Export capabilities.")]
    internal class SerializationManagerTests
    {
        private Game _game;
        private AuthenticationToken _smAuthToken;
        private const string File = "testSave.json";
        private const string File2 = "testSave2.json";
        private readonly DateTime _testTime = DateTime.Now;

        [Test]
        public void GameImportExport()
        {
            // Nubmer of systems to generate for this test. Configurable.
            const int numSystems = 10;
            const bool generateSol = false;
            int totalSystems = generateSol ? numSystems + 1 : numSystems;

            // lets create a bad save game:

            // Check default nulls throw:
            Assert.Catch<ArgumentNullException>(() => SerializationManager.Export(null, File));
            Assert.Catch<ArgumentException>(() => SerializationManager.Export(_game, (string)null));
            Assert.Catch<ArgumentException>(() => SerializationManager.Export(_game, string.Empty));

            Assert.Catch<ArgumentException>(() => SerializationManager.ImportGame((string)null));
            Assert.Catch<ArgumentException>(() => SerializationManager.ImportGame(string.Empty));
            Assert.Catch<ArgumentNullException>(() => SerializationManager.ImportGame((Stream)null));
            
            _game = TestingUtilities.CreateTestUniverse(numSystems, _testTime, generateSol);



            // lets create a good saveGame
            SerializationManager.Export(_game, File);

            Assert.IsTrue(System.IO.File.Exists(Path.Combine(SerializationManager.GetWorkingDirectory(), File)));
            Console.WriteLine(Path.Combine(SerializationManager.GetWorkingDirectory(), File));
            // now lets give ourselves a clean game:
            _game = null;

            //and load the saved data:
            _game = SerializationManager.ImportGame(File);
            _smAuthToken = new AuthenticationToken(_game.SpaceMaster);

            Assert.AreEqual(totalSystems, _game.GetSystems(_smAuthToken).Count);
            Assert.AreEqual(_testTime, StaticRefLib.CurrentDateTime);
            List<Entity> entities = _game.GlobalManager.GetAllEntitiesWithDataBlob<FactionInfoDB>(_smAuthToken);
            Assert.AreEqual(3, entities.Count);
            entities = _game.GlobalManager.GetAllEntitiesWithDataBlob<SpeciesDB>(_smAuthToken);
            Assert.AreEqual(2, entities.Count);

            // lets check the the refs were hocked back up:
            Entity species = _game.GlobalManager.GetFirstEntityWithDataBlob<SpeciesDB>(_smAuthToken);
            NameDB speciesName = species.GetDataBlob<NameDB>();
            Assert.AreSame(speciesName.OwningEntity, species);

            // <?TODO: Expand this out to cover many more DBs, entities, and cases.
        }

        [Test]
        public void CompareLoadedGameWithOriginal() //TODO do this after a few game ticks and check the comparitiveTests again. 
        {
            //create a new game
            Game newGame = TestingUtilities.CreateTestUniverse(10, _testTime, true);

            Entity ship = newGame.GlobalManager.GetFirstEntityWithDataBlob<TransitableDB>();
            StarSystem firstSystem = newGame.Systems.First().Value;
            DateTime jumpTime = StaticRefLib.CurrentDateTime + TimeSpan.FromMinutes(1);

            //insert a jump so that we can compair timeloop dictionary
            InterSystemJumpProcessor.SetJump(newGame, jumpTime,  firstSystem, jumpTime, ship);

            // lets create a good saveGame
            SerializationManager.Export(newGame, File);
            //then load it:
            Game loadedGame = SerializationManager.ImportGame(File);

            //run some tests
            ComparitiveTests(newGame, loadedGame);
        }


        void ComparitiveTests(Game original, Game loadedGame)
        {
            StarSystem firstOriginal = original.Systems.First().Value;
            StarSystem firstLoaded = loadedGame.Systems.First().Value;

            Assert.AreEqual(firstOriginal.Guid, firstLoaded.Guid);
            Assert.AreEqual(firstOriginal.NameDB.DefaultName, firstLoaded.NameDB.DefaultName);
  
            Assert.AreEqual(original.GamePulse, loadedGame.GamePulse);

            Assert.AreEqual(firstOriginal.ManagerSubpulses.GetTotalNumberOfProceses(), firstLoaded.ManagerSubpulses.GetTotalNumberOfProceses());
            Assert.AreEqual(firstOriginal.ManagerSubpulses.GetInteruptDateTimes(), firstLoaded.ManagerSubpulses.GetInteruptDateTimes());

            Assert.AreEqual(original.GlobalManager.NumberOfGlobalEntites, loadedGame.GlobalManager.NumberOfGlobalEntites);

            var originalEntitiesList = original.GlobalManager.Entities.Where(x => x != null).ToList();
            var loadedEntitiesList = loadedGame.GlobalManager.Entities.Where(x => x != null).ToList();

            Assert.AreEqual(originalEntitiesList.Count, loadedEntitiesList.Count);
            Assert.AreEqual(originalEntitiesList.Select(x => x.Guid).OrderBy(x => x).ToList(), loadedEntitiesList.Select(x => x.Guid).OrderBy(x => x).ToList());
        }


        [Test]
        public void EntityImportExport()
        {
            // Ensure we have a test universe.
            _game = TestingUtilities.CreateTestUniverse(10);
            _smAuthToken = new AuthenticationToken(_game.SpaceMaster);

            Assert.NotNull(_game);

            // Choose a random system.
            var rand = new Random();
            List<StarSystem> systems = _game.GetSystems(_smAuthToken);
            int systemIndex = rand.Next(systems.Count - 1);
            StarSystem system = systems[systemIndex];

            // Export/Reinport all system bodies in that system.

            foreach (Entity entity in system.GetAllEntitiesWithDataBlob<SystemBodyInfoDB>(_smAuthToken))
            {
                string jsonString = SerializationManager.Export(_game, entity);

                // Clone the entity for later comparison.
                ProtoEntity clone = entity.Clone();

                // Destroy the entity.
                entity.Destroy();

                // Ensure the entity was destroyed.
                Entity foundEntity;
                Assert.IsFalse(system.FindEntityByGuid(clone.Guid, out foundEntity));

                // Import the entity back into the manager.
                Entity importedEntity = SerializationManager.ImportEntityJson(_game, jsonString, system);

                // Ensure the imported entity is valid
                Assert.IsTrue(importedEntity.IsValid);
                // Check to find the guid.
                Assert.IsTrue(system.FindEntityByGuid(clone.Guid, out foundEntity));
                // Check the ID imported correctly.
                Assert.AreEqual(clone.Guid, importedEntity.Guid);
                // Check the datablobs imported correctly.
                Assert.AreEqual(clone.DataBlobs.Where(dataBlob => dataBlob != null).ToList().Count, importedEntity.DataBlobs.Count);
                // Check the manager is the same.
                Assert.AreEqual(system, importedEntity.Manager);
            }
        }

        [Test]
        public void TestProceduralStarSystemImportExport()
        {
            _game = TestingUtilities.CreateTestUniverse(1);
            _smAuthToken = new AuthenticationToken(_game.SpaceMaster);

            Assert.NotNull(_game);

            // Test each procedural system.
            List<StarSystem> systems = _game.GetSystems(_smAuthToken);
            ImportExportSystem(systems.First(), "Procedural");
        }

        [Test]
        public void SolStarSystemImportExport()
        {
            _game = TestingUtilities.CreateTestUniverse(1, true);
            _smAuthToken = new AuthenticationToken(_game.SpaceMaster);

            Assert.NotNull(_game);

            //Test with Sol.
            List<StarSystem> systems = _game.GetSystems(_smAuthToken);
            ImportExportSystem(systems.First(), "Sol");
        }

        private void ImportExportSystem(StarSystem system, string testFileSuffix = "")
        {

            //TODO: need to be able to export a system that will only save systemBodies. 
            //for a generated one, just saving the seed should suffice. 
            //for a created one we'll have to do more. 
            //also need to be able to export a players view of a system. but that would likely be a different function altogether. 
            var filename = "testSystemExport" + testFileSuffix + ".json";
            SerializationManager.Export(_game, filename, system);
            string jsonString = SerializationManager.Export(_game, system);

            int entityCount = system.GetAllEntitiesWithDataBlob<SystemBodyInfoDB>(_smAuthToken).Count;
            int nameCount = system.GetAllEntitiesWithDataBlob<NameDB>(_smAuthToken).Count;
            int orbitCount = system.GetAllEntitiesWithDataBlob<OrbitDB>(_smAuthToken).Count;

            _game = TestingUtilities.CreateTestUniverse(0);
            _smAuthToken = new AuthenticationToken(_game.SpaceMaster);

            StarSystem importedSystem = SerializationManager.ImportSystemJson(_game, jsonString);
            Assert.AreEqual(system.Guid, importedSystem.Guid);

            // See that the entities were imported.
            Assert.AreEqual(entityCount, importedSystem.GetAllEntitiesWithDataBlob<SystemBodyInfoDB>(_smAuthToken).Count);
            Assert.AreEqual(nameCount, importedSystem.GetAllEntitiesWithDataBlob<NameDB>(_smAuthToken).Count);
            Assert.AreEqual(orbitCount, importedSystem.GetAllEntitiesWithDataBlob<OrbitDB>(_smAuthToken).Count);

            // Ensure the system was added to the game's system list.
            List<StarSystem> systems = _game.GetSystems(_smAuthToken);
            Assert.AreEqual(1, systems.Count);

            // Ensure the returned value references the same system as the game's system list
            system = _game.GetSystem(_smAuthToken, system.Guid);
            Assert.AreEqual(importedSystem, system);
        }

        /// <summary>
        /// apears to test two saves to confirm that they are the same
        /// </summary>
        [Test]        
        public void SaveGameConsistency()
        {
            const int maxTries = 10;

            for (int numTries = 0; numTries < maxTries; numTries++)
            {
                TestingUtilities.CreateTestUniverse(10);
                SerializationManager.Export(_game, File);
                _game = SerializationManager.ImportGame(File);
                SerializationManager.Export(_game, File2);

                var fs1 = new FileStream(Path.Combine(SerializationManager.GetWorkingDirectory(), File), FileMode.Open);
                var fs2 = new FileStream(Path.Combine(SerializationManager.GetWorkingDirectory(), File2), FileMode.Open);

                if (fs1.Length == fs2.Length)
                {
                    // Read and compare a byte from each file until either a
                    // non-matching set of bytes is found or until the end of
                    // file1 is reached.
                    int file1Byte;
                    int file2Byte;
                    do
                    {
                        // Read one byte from each file.
                        file1Byte = fs1.ReadByte();
                        file2Byte = fs2.ReadByte();
                    } while ((file1Byte == file2Byte) && (file1Byte != -1));

                    // Close the files.
                    fs1.Close();
                    fs2.Close();

                    // Return the success of the comparison. "file1byte" is 
                    // equal to "file2byte" at this point only if the files are 
                    // the same.
                    if (file1Byte - file2Byte == 0)
                    {
                        Assert.Pass("Save Games consistent on try #" + (numTries + 1));
                    }
                }

                fs1.Close();
                fs2.Close();
            }
            Assert.Fail("SaveGameConsistency could not be verified. Please ensure saves are properly loading and saving.");
        }

        [Test]
        public void TestSingleSystemSave()
        {
            _game = TestingUtilities.CreateTestUniverse(1);
            _smAuthToken = new AuthenticationToken(_game.SpaceMaster);

            StarSystemFactory starsysfac = new StarSystemFactory(_game);
            StarSystem sol  = starsysfac.CreateSol(_game);
            StaticDataManager.ExportStaticData(sol, "solsave.json");
        }




    }
}
