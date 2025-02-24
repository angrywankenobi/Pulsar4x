﻿using NUnit.Framework;
using Pulsar4X.ECSLib;
using Pulsar4X.Orbital;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;

namespace Pulsar4X.Tests
{
    [TestFixture, Description("Entity Manager Tests")]
    class EntityManagerTests
    {
        private Game _game;
        private AuthenticationToken _smAuthToken;
        private Entity _species1;
        private Dictionary<Entity, long> _pop1;
        private Dictionary<Entity, long> _pop2;

        [SetUp]
        public void Init()
        {
            var settings = new NewGameSettings {GameName = "Test Game", StartDateTime = DateTime.Now, MaxSystems = 1};

            _game = new Game(settings);
            _smAuthToken = new AuthenticationToken(_game.SpaceMaster);
            _game.GenerateSystems(_smAuthToken, 1);
            _species1 = Entity.Create(_game.GlobalManager, Guid.Empty, new List<BaseDataBlob> {new SpeciesDB(1, 0.1, 1.9, 1.0, 0.4, 4, 14, -15, 45)});
            _pop1 = new Dictionary<Entity, long> { { _species1, 10 } };
            _pop2 = new Dictionary<Entity, long> { { _species1, 5 } };
        }
        

        [Test]
        public void TestSelfReferencingEntity()
        {
            NameDB name = new NameDB();
            Entity testEntity = Entity.Create(_game.GlobalManager, Guid.Empty);
            name.SetName(testEntity.Guid, "TestName");
            testEntity.SetDataBlob(name);

            //serialise the test entity into a mem stream
            var mStream = new MemoryStream();
            SerializationManager.Export(_game, mStream, testEntity);


            //create a second game, we're going to import this entity to here (this would be the case in a network game)
            var settings = new NewGameSettings { GameName = "Test Game2", StartDateTime = DateTime.Now, MaxSystems = 1 };
            Game game2 = new Game(settings);


            //import the entity into the second game.
            Entity clonedEntity = SerializationManager.ImportEntity(game2, mStream, game2.GlobalManager);
            mStream.Close();

            Assert.IsTrue(testEntity.GetValueCompareHash() == clonedEntity.GetValueCompareHash(), "ValueCompareHash should match");//currently valueCompareHash does not check guid of the entity. I'm undecided wheather it should or not. 
            Entity clonedTest;
            Assert.IsTrue(game2.GlobalManager.FindEntityByGuid(testEntity.Guid, out clonedTest), "Game2 should have the test entity");
            Assert.IsTrue(testEntity.Guid == clonedEntity.Guid, "ID's need to match, if we get to this assert, then we've got two entities in game2, one of them has the correct guid but no datablobs, the other has a new guid but is complete.");
            Assert.IsTrue(ReferenceEquals(clonedTest, clonedEntity),"These should be the same object" );
            Assert.IsTrue(testEntity.DataBlobs.Count == clonedTest.DataBlobs.Count);
            Assert.IsTrue(testEntity.DataBlobs.Count == clonedEntity.DataBlobs.Count); 
        }


        [Test]
        public void CreateEntity()
        {
            // create entity with no data blobs:
            Entity testEntity = Entity.Create(_game.GlobalManager, Guid.Empty);
            Assert.IsTrue(testEntity.IsValid);
            Assert.AreSame(_game.GlobalManager, testEntity.Manager);

            // Check the mask.
            Assert.AreEqual(EntityManager.BlankDataBlobMask(), testEntity.DataBlobMask);

            // Create entity with existing datablobs:
            var dataBlobs = new List<BaseDataBlob> {new OrbitDB(), new ColonyInfoDB(_pop1, Entity.InvalidEntity)};
            testEntity = Entity.Create(_game.GlobalManager, Guid.Empty, dataBlobs);
            Assert.IsTrue(testEntity.IsValid);

            // Check the mask.
            ComparableBitArray expectedMask = EntityManager.BlankDataBlobMask();
            int orbitTypeIndex = EntityManager.GetTypeIndex<OrbitDB>();
            int colonyTypeIndex = EntityManager.GetTypeIndex<ColonyInfoDB>();
            expectedMask[orbitTypeIndex] = true;
            expectedMask[colonyTypeIndex] = true;

            Assert.AreEqual(expectedMask, testEntity.DataBlobMask);

            // Create entity with existing datablobs, but provide an empty list:
            dataBlobs.Clear();
            testEntity = Entity.Create(_game.GlobalManager, Guid.Empty, dataBlobs);
            Assert.IsTrue(testEntity.IsValid);
        }

        [Test]
        public void SetDataBlobs()
        {
            Entity testEntity = Entity.Create(_game.GlobalManager, Guid.Empty);
            testEntity.SetDataBlob(new OrbitDB());
            testEntity.SetDataBlob(new ColonyInfoDB(_pop1, Entity.InvalidEntity));
            testEntity.SetDataBlob(new PositionDB(Vector3.Zero, Guid.Empty), EntityManager.GetTypeIndex<PositionDB>());

            // test bad input:
            Assert.Catch(typeof(ArgumentNullException), () =>
            {
                testEntity.SetDataBlob((OrbitDB)null); // should throw ArgumentNullException
            });
        }

        [Test]
        public void GetDataBlobsByEntity()
        {
            Entity testEntity = PopulateEntityManager();  // make sure we have something in there.

            // Get all DataBlobs of a specific entity.
            ReadOnlyCollection<BaseDataBlob> dataBlobs = testEntity.DataBlobs;
            Assert.AreEqual(2, dataBlobs.Count);

            // empty entity mean empty list.
            testEntity = Entity.Create(_game.GlobalManager, Guid.Empty);  // create empty entity.
            dataBlobs = testEntity.DataBlobs;
            Assert.AreEqual(0, dataBlobs.Count);
        }

        [Test]
        public void GetDataBlobByEntity()
        {
            Entity testEntity = PopulateEntityManager();

            // Get the Population DB of a specific entity.
            ColonyInfoDB popDB = testEntity.GetDataBlob<ColonyInfoDB>();
            Assert.IsNotNull(popDB);

            // get a DB we know the entity does not have:
            AtmosphereDB atmoDB = testEntity.GetDataBlob<AtmosphereDB>();
            Assert.IsNull(atmoDB);

            // test with invalid data blob type
            Assert.Catch(typeof(KeyNotFoundException), () =>
            {
                testEntity.GetDataBlob<BaseDataBlob>();
            });

            // and again for the second lookup type:
            // Get the Population DB of a specific entity.
            int typeIndex = EntityManager.GetTypeIndex<ColonyInfoDB>();
            popDB = testEntity.GetDataBlob<ColonyInfoDB>(typeIndex);
            Assert.IsNotNull(popDB);

            // get a DB we know the entity does not have:
            typeIndex = EntityManager.GetTypeIndex<AtmosphereDB>();
            atmoDB = testEntity.GetDataBlob<AtmosphereDB>(typeIndex);
            Assert.IsNull(atmoDB);

            // test with invalid type index:
            Assert.Catch(typeof(ArgumentOutOfRangeException), () =>
            {
                testEntity.GetDataBlob<AtmosphereDB>(-42);
            });

            // test with invalid T vs type at typeIndex
            typeIndex = EntityManager.GetTypeIndex<ColonyInfoDB>();
            Assert.Catch(typeof(InvalidCastException), () =>
            {
                testEntity.GetDataBlob<SystemBodyInfoDB>(typeIndex);
            });
        }

        [Test]
        public void RemoveEntities()
        {
            Entity testEntity = PopulateEntityManager();

            // lets check the entity at index testEntity
            ReadOnlyCollection<BaseDataBlob> testList = testEntity.DataBlobs;
            Assert.AreEqual(2, testList.Count);  // should have 2 datablobs.

            // Remove an entity.
            testEntity.Destroy();

            // now lets see if the entity is still there:
            testList = testEntity.DataBlobs;
            Assert.AreEqual(0, testList.Count);  // should have 0 datablobs.
   
            Assert.IsFalse(testEntity.IsValid);

            // Try to get a bad mask.
            Assert.AreEqual(testEntity.DataBlobMask, EntityManager.BlankDataBlobMask());

            // Now try to remove the entity. Again.
            Assert.Catch<InvalidOperationException>(testEntity.Destroy);
        }

        [Test]
        public void RemoveDataBlobs()
        {
            // a little setup:
            Entity testEntity = Entity.Create(_game.GlobalManager, Guid.Empty);
            testEntity.SetDataBlob(new ColonyInfoDB(_pop1, Entity.InvalidEntity));

            Assert.IsTrue(testEntity.GetDataBlob<ColonyInfoDB>() != null);  // check that it has the data blob
            testEntity.RemoveDataBlob<ColonyInfoDB>();                     // Remove a data blob
            Assert.IsTrue(testEntity.GetDataBlob<ColonyInfoDB>() == null); // now check that it doesn't

            // now lets try remove it again:
            Assert.Catch<InvalidOperationException>(testEntity.RemoveDataBlob<ColonyInfoDB>);

            // cannot remove baseDataBlobs, invalid data blob type:
            Assert.Catch(typeof(KeyNotFoundException), () =>
            {
                testEntity.RemoveDataBlob<BaseDataBlob>();  
            });


            // reset:
            testEntity.SetDataBlob(new ColonyInfoDB(_pop1, Entity.InvalidEntity));
            int typeIndex = EntityManager.GetTypeIndex<ColonyInfoDB>();

            Assert.IsTrue(testEntity.GetDataBlob<ColonyInfoDB>() != null);  // check that it has the data blob
            testEntity.RemoveDataBlob(typeIndex);              // Remove a data blob
            Assert.IsTrue(testEntity.GetDataBlob<ColonyInfoDB>() == null); // now check that it doesn't

            // now lets try remove it again:
            Assert.Catch<InvalidOperationException>(() => testEntity.RemoveDataBlob(typeIndex));

            // and an invalid typeIndex:
            Assert.Catch(typeof(ArgumentException), () => testEntity.RemoveDataBlob(-42));

            // now lets try an invalid entity:
            testEntity.Destroy();
            Assert.Catch<InvalidOperationException>(() => testEntity.RemoveDataBlob(typeIndex));

        }

        [Test]
        public void EntityLookup()
        {
            PopulateEntityManager();

            // Find all entities with a specific DataBlob.
            List<Entity> entities = _game.GlobalManager.GetAllEntitiesWithDataBlob<ColonyInfoDB>(_smAuthToken);
            Assert.AreEqual(2, entities.Count);

            // again, but look for a datablob that no entity has:
            entities = _game.GlobalManager.GetAllEntitiesWithDataBlob<AtmosphereDB>(_smAuthToken);
            Assert.AreEqual(0, entities.Count);

            // check with invalid data blob type:
            Assert.Catch(typeof(KeyNotFoundException), () => _game.GlobalManager.GetAllEntitiesWithDataBlob<BaseDataBlob>(_smAuthToken));

            // now lets lookup using a mask:
            ComparableBitArray dataBlobMask = EntityManager.BlankDataBlobMask();
            dataBlobMask.Set(EntityManager.GetTypeIndex<ColonyInfoDB>(), true);
            dataBlobMask.Set(EntityManager.GetTypeIndex<OrbitDB>(), true);
            entities = _game.GlobalManager.GetAllEntitiesWithDataBlobs(_smAuthToken, dataBlobMask);
            Assert.AreEqual(2, entities.Count);

            // and with a mask that will not match any entities:
            dataBlobMask.Set(EntityManager.GetTypeIndex<AtmosphereDB>(), true);
            entities = _game.GlobalManager.GetAllEntitiesWithDataBlobs(_smAuthToken, dataBlobMask);
            Assert.AreEqual(0, entities.Count);

            // and an empty mask:
            dataBlobMask = EntityManager.BlankDataBlobMask();
            entities = _game.GlobalManager.GetAllEntitiesWithDataBlobs(_smAuthToken, dataBlobMask);
            Assert.AreEqual(3, entities.Count); // this is counter intuitive... but it is what happens.

            // test bad mask:
            ComparableBitArray badMask = new ComparableBitArray(4242); // use a big number so we never rach that many data blobs.
            Assert.Catch(typeof(ArgumentException), () => _game.GlobalManager.GetAllEntitiesWithDataBlobs(_smAuthToken, badMask));

            Assert.Catch(typeof(ArgumentNullException), () => _game.GlobalManager.GetAllEntitiesWithDataBlobs(_smAuthToken, null));


            // now lets just get the one entity:
            Entity testEntity = _game.GlobalManager.GetFirstEntityWithDataBlob<ColonyInfoDB>(_smAuthToken);
            Assert.IsTrue(testEntity.IsValid);

            // lookup an entity that does not exist:
            testEntity = _game.GlobalManager.GetFirstEntityWithDataBlob<AtmosphereDB>(_smAuthToken);
            Assert.IsFalse(testEntity.IsValid);

            // try again with incorrect type:
            Assert.Catch(typeof(KeyNotFoundException), () =>
            {
                _game.GlobalManager.GetFirstEntityWithDataBlob<BaseDataBlob>(_smAuthToken);
            });


            // now lets just get the one entity, but use a different function to do it:
            int type = EntityManager.GetTypeIndex<ColonyInfoDB>();
            testEntity = _game.GlobalManager.GetFirstEntityWithDataBlob(_smAuthToken, type);
            Assert.IsTrue(testEntity.IsValid);

            // lookup an entity that does not exist:
            type = EntityManager.GetTypeIndex<AtmosphereDB>();
            testEntity = _game.GlobalManager.GetFirstEntityWithDataBlob(_smAuthToken, type);
            Assert.IsFalse(testEntity.IsValid);

            // try again with incorrect type index:
            Assert.AreEqual(Entity.InvalidEntity, _game.GlobalManager.GetFirstEntityWithDataBlob(_smAuthToken, 4242));
        }

        [Test]
        public void EntityGuid()
        {
            Entity foundEntity;
            Entity testEntity = Entity.Create(_game.GlobalManager, Guid.Empty);

            Assert.IsTrue(testEntity.IsValid);
            // Check ID local lookup.
            Assert.IsTrue(_game.GlobalManager.TryGetEntityByGuid(testEntity.Guid, out foundEntity));
            Assert.IsTrue(testEntity == foundEntity);
            
            // Check ID global lookup.
            Assert.IsTrue(_game.GlobalManager.FindEntityByGuid(testEntity.Guid, out foundEntity));
            Assert.AreEqual(testEntity, foundEntity);

            // and a removed entity:
            testEntity.Destroy();
            Assert.IsFalse(testEntity.IsValid);

            // Check bad ID lookups.
            Assert.IsFalse(_game.GlobalManager.TryGetEntityByGuid(Guid.Empty, out foundEntity));
            Assert.IsFalse(_game.GlobalManager.FindEntityByGuid(Guid.Empty, out foundEntity));
        }

        [Test]
        public void EntityTransfer()
        {
            EntityManager manager2 = _game.GetSystems(_smAuthToken).First();

            PopulateEntityManager();

            // Get an entity from the manager.
            Entity testEntity = _game.GlobalManager.GetFirstEntityWithDataBlob<OrbitDB>(_smAuthToken);
            // Ensure we got a valid entity.
            Assert.IsTrue(testEntity.IsValid);
            // Store it's datablobs for later.
            ReadOnlyCollection<BaseDataBlob> testEntityDataBlobs = testEntity.DataBlobs;
            
            // Store the current GUID.
            Guid entityGuid = testEntity.Guid;

            // Try to transfer to a null Manager.
            Assert.Catch<ArgumentNullException>(() => testEntity.Transfer(null));

            // Transfer the entity to a Entity.CreateManager
            testEntity.Transfer(manager2);

            // Ensure the original manager no longer has the entity.
            Entity foundEntity;
            Assert.IsFalse(_game.GlobalManager.TryGetEntityByGuid(entityGuid, out foundEntity));

            // Ensure the new manager has the entity.
            Assert.IsTrue(testEntity.Manager == manager2);
            Assert.IsTrue(manager2.TryGetEntityByGuid(entityGuid, out foundEntity));
            Assert.AreSame(testEntity, foundEntity);

            // Get the transferredEntity's datablobs.
            ReadOnlyCollection<BaseDataBlob> transferredEntityDataBlobs = testEntity.DataBlobs;
            
            // Compare the old datablobs with the new datablobs.
            foreach (BaseDataBlob testEntityDataBlob in testEntityDataBlobs)
            {
                bool matchFound = false;
                foreach (BaseDataBlob transferredDataBlob in transferredEntityDataBlobs)
                {
                    if (ReferenceEquals(testEntityDataBlob, transferredDataBlob))
                    {
                        matchFound = true;
                        break;
                    }
                }
                Assert.IsTrue(matchFound);
            }

            // Try to transfer an invalid entity.
            testEntity.Destroy();
            Assert.Catch<InvalidOperationException>(() => testEntity.Transfer(_game.GlobalManager));

        }

        [Test]
        public void TypeIndexTest()
        {
            int typeIndex;
            Assert.Catch<ArgumentNullException>(() => EntityManager.TryGetTypeIndex(null, out typeIndex));
            Assert.Catch<KeyNotFoundException>(() => EntityManager.GetTypeIndex<BaseDataBlob>());

            Assert.IsTrue(EntityManager.TryGetTypeIndex(typeof(OrbitDB), out typeIndex));
            Assert.AreEqual(EntityManager.GetTypeIndex<OrbitDB>(), typeIndex);
        }

        [Test]
        public void HasDataBlobTests()
        {
            Entity testEntity = Entity.Create(_game.GlobalManager, Guid.Empty);
            testEntity.SetDataBlob(new OrbitDB());
            testEntity.SetDataBlob(new ColonyInfoDB(_pop1, Entity.InvalidEntity));
            testEntity.SetDataBlob(new PositionDB(Vector3.Zero, Guid.Empty), EntityManager.GetTypeIndex<PositionDB>());

            Assert.True(testEntity.HasDataBlob<OrbitDB>(), "This entity should have an OrbitDB");
            Assert.False(testEntity.HasDataBlob<VolumeStorageDB>(), "This entity should NOT have a VolumeStorageDB");

            int typeIndex_OrbitDB;
            Assert.True(EntityManager.TryGetTypeIndex(typeof(OrbitDB), out typeIndex_OrbitDB), "We should be able to find the type index for OrbitDB");

            int typeIndex_CargoStorageDB;
            Assert.True(EntityManager.TryGetTypeIndex(typeof(VolumeStorageDB), out typeIndex_CargoStorageDB), "We should be able to find the type index for CargoStorageDB");

            Assert.True(testEntity.HasDataBlob<OrbitDB>(), "This entity should have an OrbitDB");
            Assert.False(testEntity.HasDataBlob<VolumeStorageDB>(), "This entity should NOT have a CargoStorageDB");

            Assert.True(testEntity.HasDataBlob(typeIndex_OrbitDB), "This entity should have an OrbitDB by index");
            Assert.False(testEntity.HasDataBlob(typeIndex_CargoStorageDB), "This entity should NOT have a CargoStorageDB by index" );

        }

        #region Extra Init Stuff

        /// <summary>
        /// This functions creates 3 entities with a total of 5 data blobs (3 orbits and 2 populations).
        /// </summary>
        /// <returns>It returns a reference to the first entity (containing 1 orbit and 1 pop)</returns>
        private Entity PopulateEntityManager()
        {
            // Clear out any previous test results.
            _game.GlobalManager.Clear();

            // Create an entity with individual DataBlobs.
            Entity testEntity = Entity.Create(_game.GlobalManager, Guid.Empty);
            testEntity.SetDataBlob(new OrbitDB());
            testEntity.SetDataBlob(new ColonyInfoDB(_pop1, Entity.InvalidEntity));

            // Create an entity with a DataBlobList.
            var dataBlobs = new List<BaseDataBlob> { new OrbitDB() };
            Entity.Create(_game.GlobalManager, Guid.Empty, dataBlobs);

            // Create one more, just for kicks.
            dataBlobs = new List<BaseDataBlob> { new OrbitDB(), new ColonyInfoDB(_pop2, Entity.InvalidEntity) };
            Entity.Create(_game.GlobalManager, Guid.Empty, dataBlobs);

            return testEntity;
        }

        #endregion

    }
}
