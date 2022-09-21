using System;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Pulsar4X.Orbital;

namespace Pulsar4X.ECSLib
{
    public class OrbitDB : TreeHierarchyDB, IGetValuesHash
    {

        protected KeplerElements elements;

        /// <summary>
        /// Stored in Meters
        /// </summary>
        [JsonProperty]
        public double SemiMajorAxis => elements.SemiMajorAxis;

        /// <summary>
        /// Eccentricity of orbit.
        /// Shape of the orbit. 0 = perfectly circular, 1 = parabolic.
        /// </summary>
        [PublicAPI]
        [JsonProperty]
        public double Eccentricity => elements.Eccentricity;

        /// <summary>
        /// Angle between the orbit and the flat reference plane.
        /// in radians.
        /// </summary>
        [PublicAPI]
        [JsonProperty]
        public double Inclination => elements.Inclination;

        /// <summary>
        /// Horizontal orientation of the point where the orbit crosses
        /// the reference frame stored in radians.
        /// </summary>
        [PublicAPI]
        [JsonProperty]
        public double LongitudeOfAscendingNode => elements.LoAN;


        /// <summary>
        /// Angle from the Ascending Node to the Periapsis stored in radians.
        /// </summary>
        [PublicAPI]
        [JsonProperty]
        public double ArgumentOfPeriapsis => elements.AoP;


        /// <summary>
        /// Definition of the position of the body in the orbit at the reference time
        /// epoch. Mathematically convenient angle does not correspond to a real angle.
        /// Stored in degrees.
        /// </summary>
        [PublicAPI]
        [JsonProperty]
        public double MeanAnomalyAtEpoch => elements.MeanAnomalyAtEpoch;

        /// <summary>
        /// reference time. Orbital parameters are stored relative to this reference.
        /// </summary>
        [PublicAPI]
        [JsonProperty]
        public DateTime Epoch => elements.Epoch;

        /// <summary>
        /// 2-Body gravitational parameter of system in m^3/s^2
        /// </summary>
        [PublicAPI]
        public double GravitationalParameter_m3S2 => elements.StandardGravParameter;

        public double SOI_m { get; protected set; } = double.MaxValue;


        /// <summary>
        /// Orbital Period of orbit.
        /// </summary>
        [PublicAPI]
        public TimeSpan OrbitalPeriod => TimeSpan.FromSeconds(elements.Period);

        /// <summary>
        /// Mean Motion of orbit. in Radians/Sec.
        /// </summary>
        [PublicAPI]
        public double MeanMotion => elements.MeanMotion;

        /// <summary>
        /// Point in orbit furthest from the ParentBody. Measured in AU.
        /// </summary>
        [PublicAPI]
        public double Apoapsis => elements.Apoapsis;

        /// <summary>
        /// Point in orbit closest to the ParentBody. Measured in AU.
        /// </summary>
        [PublicAPI]
        public double Periapsis => elements.Periapsis;

        /// <summary>
        /// Stationary orbits don't have all of the data to update. They always return (0, 0).
        /// </summary>
        [PublicAPI]
        [JsonProperty]
        public bool IsStationary { get; private set; } = false;

        [JsonProperty]
        internal double _parentMass;
        [JsonProperty]
        internal double _myMass;

        #region Construction Interface

        
        /// <summary>
        /// Creates on Orbit at current location from a given velocity
        /// </summary>
        /// <returns>The Orbit Does not attach the OrbitDB to the entity!</returns>
        /// <param name="parent">Parent. must have massdb</param>
        /// <param name="entity">Entity. must have massdb</param>
        /// <param name="velocity_m">Velocity in meters.</param>
        public static OrbitDB FromVelocity(Entity parent, Entity entity, Vector3 velocity_m, DateTime atDateTime)
        {
            var myMass = entity.GetDataBlob<MassVolumeDB>().MassDry;

            //var epoch1 = parent.Manager.ManagerSubpulses.StarSysDateTime; //getting epoch from here is incorrect as the local datetime doesn't change till after the subpulse.

            //var parentPos = OrbitProcessor.GetAbsolutePosition_AU(parent.GetDataBlob<OrbitDB>(), atDateTime); //need to use the parent position at the epoch
            var posdb = entity.GetDataBlob<PositionDB>();
            posdb.SetParent(parent);
            var relativePos = posdb.RelativePosition;//entity.GetDataBlob<PositionDB>().AbsolutePosition_AU - parentPos;
            if (relativePos.Length() > parent.GetSOI_m())
                throw new Exception("Entity not in target SOI");

            //var sgp = UniversalConstants.Science.GravitationalConstant * (myMass + parentMass) / 3.347928976e33;
            var sgp_m = GeneralMath.StandardGravitationalParameter(myMass + parent.GetDataBlob<MassVolumeDB>().MassDry);
            var ke_m = OrbitMath.KeplerFromPositionAndVelocity(sgp_m, relativePos, velocity_m, atDateTime);


            OrbitDB orbit = FromKeplerElements(parent, myMass, ke_m, atDateTime);

            var pos = orbit.GetPosition(atDateTime);
            var d = (pos - relativePos).Length();
            if (d > 1)
            {
                var e = new Event(atDateTime, "Positional difference of " + Stringify.Distance(d) + " when creating orbit from velocity");
                e.Entity = entity;
                e.SystemGuid = entity.Manager.ManagerGuid;
                e.EventType = EventType.Opps;
                //e.Faction =  entity.FactionOwner;
                StaticRefLib.EventLog.AddEvent(e);

                //other info:
                var keta = Angle.ToDegrees(ke_m.TrueAnomalyAtEpoch);
                var obta = Angle.ToDegrees(orbit.GetTrueAnomaly(atDateTime));
                var tadif = Angle.ToDegrees(Angle.DifferenceBetweenRadians(keta, obta));
                var pos1 = orbit.GetPosition(atDateTime);
                var pos2 = orbit.GetPosition_m(ke_m.TrueAnomalyAtEpoch);
                var d2 = (pos1 - pos2).Length();
            }
            
            
            return orbit;
        }
        
                
        /// <summary>
        /// In Meters!
        /// </summary>
        /// <param name="parent"></param>
        /// <param name="myMass"></param>
        /// <param name="parentMass"></param>
        /// <param name="sgp_m"></param>
        /// <param name="position_m"></param>
        /// <param name="velocity_m"></param>
        /// <param name="atDateTime"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static OrbitDB FromVector(Entity parent, double myMass, double parentMass, double sgp_m, Vector3 position_m, Vector3 velocity_m, DateTime atDateTime)
        {
            if (position_m.Length() > parent.GetSOI_AU())
                throw new Exception("Entity not in target SOI");
            //var sgp  = UniversalConstants.Science.GravitationalConstant * (myMass + parentMass) / 3.347928976e33;
            var ke = OrbitMath.KeplerFromPositionAndVelocity(sgp_m, position_m, velocity_m, atDateTime);

            return FromKeplerElements(parent, myMass, ke, atDateTime);
        }

        /// <summary>
        /// In Meters
        /// </summary>
        /// <param name="parent"></param>
        /// <param name="myMass"></param>
        /// <param name="ke">in m</param>
        /// <param name="atDateTime"></param>
        /// <returns></returns>
        public static OrbitDB FromKeplerElements(Entity parent, double myMass, KeplerElements ke, DateTime atDateTime)
        {

            OrbitDB orbit = new OrbitDB(parent)
            {
                elements = ke,
                _parentMass = parent.GetDataBlob<MassVolumeDB>().MassDry,
                _myMass = myMass

            };
            
            orbit.IsStationary = false;
            orbit.CalculateExtendedParameters();
            return orbit;
        }

        /// <summary>
        /// Circular orbit from position. 
        /// </summary>
        /// <param name="parent"></param>
        /// <param name="obj"></param>
        /// <param name="atDatetime"></param>
        /// <returns></returns>
        public static OrbitDB FromPosition(Entity parent, Entity obj, DateTime atDatetime)
        {
            
            var parpos = parent.GetDataBlob<PositionDB>();
            var objPos = obj.GetDataBlob<PositionDB>();
            var ralpos = objPos.AbsolutePosition - parpos.AbsolutePosition;
            var r = ralpos.Length();
            var i = Math.Atan2(ralpos.Z, r);
            var m0 = Math.Atan2(ralpos.Y, ralpos.X);
            var orbit = new OrbitDB(parent)
            {
                SemiMajorAxis = r,
                Eccentricity = 0,
                Inclination = i,
                LongitudeOfAscendingNode = 0,
                ArgumentOfPeriapsis = 0,
                MeanAnomalyAtEpoch = m0,
                Epoch = atDatetime,

                _parentMass = parent.GetDataBlob<MassVolumeDB>().MassDry,
                _myMass = obj.GetDataBlob<MassVolumeDB>().MassDry
                
            };
            orbit.IsStationary = false;
            orbit.CalculateExtendedParameters();

            return orbit;
        }
        
        /// <summary>
        /// Circular orbit from position. 
        /// </summary>
        /// <param name="parent"></param>
        /// <param name="objPos">ralitive to parent</param>
        /// <param name="objMass"></param>
        /// <param name="atDatetime"></param>
        /// <returns></returns>
        public static OrbitDB FromPosition(Entity parent, Vector3 objPos, double objMass, DateTime atDatetime)
        {
            
            var parpos = parent.GetDataBlob<PositionDB>();
            
            
            var r = objPos.Length();
            var i = Math.Atan2(objPos.Z, r);
            var m0 = Math.Atan2(objPos.Y, objPos.X);
            var orbit = new OrbitDB(parent, parent.GetDataBlob<MassVolumeDB>().MassDry, objMass, r, 0, i, 0, 0, m0, atDatetime)
            {
                SemiMajorAxis = r,
                Eccentricity = 0,
                Inclination = i,
                LongitudeOfAscendingNode = 0,
                ArgumentOfPeriapsis = 0,
                MeanAnomalyAtEpoch = m0,
                Epoch = atDatetime,

                _parentMass = parent.GetDataBlob<MassVolumeDB>().MassDry,
                _myMass = objMass
                
            };
            orbit.IsStationary = false;
            orbit.CalculateExtendedParameters();

            return orbit;
        }

        /// <summary>
        /// Returns an orbit representing the defined parameters.
        /// </summary>
        /// <param name="semiMajorAxis_AU">SemiMajorAxis of orbit in AU.</param>
        /// <param name="eccentricity">Eccentricity of orbit.</param>
        /// <param name="inclination">Inclination of orbit in degrees.</param>
        /// <param name="longitudeOfAscendingNode">Longitude of ascending node in degrees.</param>
        /// <param name="longitudeOfPeriapsis">Longitude of periapsis in degrees.</param>
        /// <param name="meanLongitude">Longitude of object at epoch in degrees.</param>
        /// <param name="epoch">reference time for these orbital elements.</param>
        public static OrbitDB FromMajorPlanetFormat([NotNull] Entity parent, double parentMass, double myMass, double semiMajorAxis_AU, double eccentricity, double inclination,
                                                    double longitudeOfAscendingNode, double longitudeOfPeriapsis, double meanLongitude, DateTime epoch)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            // http://en.wikipedia.org/wiki/Longitude_of_the_periapsis
            double argumentOfPeriapsis = longitudeOfPeriapsis - longitudeOfAscendingNode;
            // http://en.wikipedia.org/wiki/Mean_longitude
            double meanAnomaly = meanLongitude - (longitudeOfAscendingNode + argumentOfPeriapsis);
            double sma_m = Distance.AuToMt(semiMajorAxis_AU);
            return new OrbitDB(parent, parentMass, myMass, sma_m, eccentricity, Angle.ToRadians(inclination), Angle.ToRadians(longitudeOfAscendingNode), Angle.ToRadians(argumentOfPeriapsis), meanAnomaly, epoch);
        }

        /// <summary>
        /// Returns an orbit representing the defined parameters.
        /// </summary>
        /// <param name="semiMajorAxis_AU">SemiMajorAxis of orbit in AU.</param>
        /// <param name="eccentricity">Eccentricity of orbit.</param>
        /// <param name="inclination">Inclination of orbit in degrees.</param>
        /// <param name="longitudeOfAscendingNode">Longitude of ascending node in degrees.</param>
        /// <param name="argumentOfPeriapsis">Argument of periapsis in degrees.</param>
        /// <param name="meanAnomaly">Mean Anomaly in degrees.</param>
        /// <param name="epoch">reference time for these orbital elements.</param>
        public static OrbitDB FromAsteroidFormat([NotNull] Entity parent, double parentMass, double myMass, double semiMajorAxis_AU, double eccentricity, double inclination,
                                                double longitudeOfAscendingNode, double argumentOfPeriapsis, double meanAnomaly, DateTime epoch)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            double sma_m = Distance.AuToMt(semiMajorAxis_AU);

			return new OrbitDB(parent, parentMass, myMass, sma_m, eccentricity, Angle.ToRadians(inclination), Angle.ToRadians(longitudeOfAscendingNode), Angle.ToRadians(argumentOfPeriapsis), meanAnomaly, epoch);
        }

        internal OrbitDB(Entity parent, double parentMass, double myMass, double semiMajorAxis_m, double eccentricity, double inclination,
                        double longitudeOfAscendingNode, double argumentOfPeriapsis, double meanAnomaly, DateTime epoch)
            : base(parent)
        {
            IsStationary = false;
            
            elements.SemiMajorAxis = semiMajorAxis_m;
            elements.Eccentricity = eccentricity;
            elements.Inclination = inclination;
            elements.LoAN= longitudeOfAscendingNode;
            elements.AoP = argumentOfPeriapsis;
            elements.MeanAnomalyAtEpoch = meanAnomaly;
            elements.Epoch = epoch;

            _parentMass = parentMass;
            _myMass = myMass;

            CalculateExtendedParameters();
        }

        public OrbitDB()
            : base(null)
        {
            IsStationary = true;
        }

        public OrbitDB(Entity parent)
            : base(parent)
        {
            IsStationary = false;
        }
        
        public OrbitDB(OrbitUpdateOftenDB orbit) : base(orbit.Parent)
        {
            if (orbit.IsStationary)
            {
                IsStationary = true;
                return;
            }

            elements = orbit.elements;
            _parentMass = orbit._parentMass;
            _myMass = orbit._myMass;
        }

        public OrbitDB(OrbitDB toCopy)
            : base(toCopy.Parent)
        {
            if (toCopy.IsStationary)
            {
                IsStationary = true;
                return;
            }

            elements = toCopy.elements;
            _parentMass = toCopy._parentMass;
            _myMass = toCopy._myMass;
        }
        #endregion

        protected void CalculateExtendedParameters()
        {
            if (IsStationary)
            {
                return;
            }
            // Calculate extended parameters.
            // http://en.wikipedia.org/wiki/Standard_gravitational_parameter#Two_bodies_orbiting_each_other
            elements.StandardGravParameter = GeneralMath.StandardGravitationalParameter(_parentMass + _myMass);

            elements.CalculateExtendedParameters();

            SOI_m = OrbitMath.GetSOI(SemiMajorAxis, _myMass, _parentMass);

        }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            CalculateExtendedParameters();
        }

        public KeplerElements GetElements()
        {
            return elements;
			return ke;
        }

        public override object Clone()
        {
            return new OrbitDB(this);
        }


        internal override void OnSetToEntity()
        {
            if (OwningEntity.HasDataBlob<NewtonMoveDB>())
            {
                OwningEntity.RemoveDataBlob<NewtonMoveDB>();
            }
            if (OwningEntity.HasDataBlob<OrbitUpdateOftenDB>())
            {
                OwningEntity.RemoveDataBlob<OrbitUpdateOftenDB>();
            }
            if (OwningEntity.HasDataBlob<WarpMovingDB>())
            {
                OwningEntity.RemoveDataBlob<WarpMovingDB>();
            }
        }
        

        #region ISensorCloneInterface



        public void SensorUpdate(SensorInfoDB sensorInfo)
        {
            UpdateFromSensorInfo(sensorInfo.DetectedEntity.GetDataBlob<OrbitDB>(), sensorInfo);
        }

        OrbitDB(OrbitDB toCopy, SensorInfoDB sensorInfo, Entity parentEntity) : base(parentEntity)
        {

            elements.Epoch = toCopy.Epoch; //This should stay the same
            IsStationary = toCopy.IsStationary;
            UpdateFromSensorInfo(toCopy, sensorInfo);

        }

        void UpdateFromSensorInfo(OrbitDB actualDB, SensorInfoDB sensorInfo)
        {
            //var quality = sensorInfo.HighestDetectionQuality.detectedSignalQuality.Percent; //quality shouldn't affect positioning. 
            double signalBestMagnatude = sensorInfo.HighestDetectionQuality.SignalStrength_kW;
            double signalNowMagnatude = sensorInfo.LatestDetectionQuality.SignalStrength_kW;

            DateTime epoch = elements.Epoch;
            elements = actualDB.elements;
            elements.Epoch = epoch;
            _parentMass = actualDB._parentMass;
            _myMass = actualDB._myMass;
            CalculateExtendedParameters();
        }

        public int GetValueCompareHash(int hash = 17)
        {
            hash = Misc.ValueHash(SemiMajorAxis, hash);
            hash = Misc.ValueHash(Eccentricity, hash);


            return hash;
        }

        #endregion
    }

    /// <summary>
    /// This allows us to update an orbit fairly frequently, however it's a bit brittle, especialy where it interacts with treeheirarchy.
    /// </summary>
    public class OrbitUpdateOftenDB : OrbitDB
    {

        [JsonConstructor]
        private OrbitUpdateOftenDB()
        {
        }

        public OrbitUpdateOftenDB(OrbitDB orbit) : base(orbit) {}
        
        internal override void OnSetToEntity()
        {
            if (OwningEntity.HasDataBlob<OrbitDB>())
            {
                OwningEntity.RemoveDataBlob<OrbitDB>();
            }
            if (OwningEntity.HasDataBlob<NewtonMoveDB>())
            {
                OwningEntity.RemoveDataBlob<NewtonMoveDB>();
            }
            if (OwningEntity.HasDataBlob<WarpMovingDB>())
            {
                OwningEntity.RemoveDataBlob<WarpMovingDB>();
            }
        }

    }
}
