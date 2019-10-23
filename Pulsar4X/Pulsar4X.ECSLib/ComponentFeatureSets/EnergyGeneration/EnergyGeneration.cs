using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Pulsar4X.ECSLib.ComponentFeatureSets.CargoStorage;

namespace Pulsar4X.ECSLib
{
    public class EnergyGenerationAtb : IComponentDesignAttribute
    {
        public Guid FuelType; //min or mat.
        
        public double FuelUsedAtMax;  //KgPerS
        
        public Guid EnergyTypeID;
        
        public double PowerOutputMax; //Mw

        public double Lifetime;
        
        public EnergyGenerationAtb(Guid fueltype, double fuelUsedAtMax, Guid energyTypeID, double powerOutputMax, double lifetime)
        {
            FuelType = fueltype;
            PowerOutputMax = powerOutputMax;
            FuelUsedAtMax = fuelUsedAtMax;
            EnergyTypeID = energyTypeID;
            Lifetime = lifetime;
        }

        public void OnComponentInstallation(Entity parentEntity, ComponentInstance componentInstance)
        {
            Guid resourceID = EnergyTypeID;
            ICargoable energyCargoable = StaticRefLib.StaticData.GetICargoable(resourceID);
            EntityEnergyGenAbilityDB entityGenDB;
            if (!parentEntity.HasDataBlob<EntityEnergyGenAbilityDB>())
            {
                entityGenDB = new EntityEnergyGenAbilityDB(parentEntity.StarSysDateTime);
                entityGenDB.EnergyType = energyCargoable;
                parentEntity.SetDataBlob(entityGenDB);
                
                
            }
            else
            {
                entityGenDB = parentEntity.GetDataBlob<EntityEnergyGenAbilityDB>();


                if (entityGenDB.EnergyType == null)
                    entityGenDB.EnergyType = energyCargoable;
                else if(entityGenDB.EnergyType != energyCargoable)//this is just to reduce complexity. we can add this ability later.
                    throw new Exception("PrimeEntity cannot use two different energy types");
                if (entityGenDB.TotalFuelUseAtMax.type == Guid.Empty)
                    entityGenDB.TotalFuelUseAtMax.type = FuelType;
                else if(entityGenDB.TotalFuelUseAtMax.type != FuelType)
                    throw new Exception("PrimeEntity cannot have power plants that use different fuel types");
            }

            entityGenDB.TotalOutputMax += PowerOutputMax;
            double maxUse = entityGenDB.TotalFuelUseAtMax.maxUse + FuelUsedAtMax;
            entityGenDB.TotalFuelUseAtMax = (FuelType, maxUse);
            entityGenDB.LocalFuel = maxUse * Lifetime;
            
            //add enough energy store for 1s of running. 
            if (entityGenDB.EnergyStoreMax.ContainsKey(EnergyTypeID))
            {
                entityGenDB.EnergyStoreMax[EnergyTypeID] += PowerOutputMax;
            }
            else
            {
                entityGenDB.EnergyStored[EnergyTypeID] = 0;
                entityGenDB.EnergyStoreMax[EnergyTypeID] = PowerOutputMax;
            }

        }
    }

    public class EnergyStoreAtb : IComponentDesignAttribute
    {
        //<type, amount>
        public Guid EnergyTypeID;
        public double MaxStore;

        public EnergyStoreAtb(Guid energyTypeID, double maxStore)
        {
            EnergyTypeID = energyTypeID;
            MaxStore = maxStore;
        }

        public void OnComponentInstallation(Entity parentEntity, ComponentInstance componentInstance)
        {
            EntityEnergyGenAbilityDB entityGenDB;
            
            if (!parentEntity.HasDataBlob<EntityEnergyGenAbilityDB>())
            {
                entityGenDB = new EntityEnergyGenAbilityDB(parentEntity.StarSysDateTime);
                parentEntity.SetDataBlob(entityGenDB);
            }
            else
            {
                entityGenDB = parentEntity.GetDataBlob<EntityEnergyGenAbilityDB>();
            }
            if (entityGenDB.EnergyStoreMax.ContainsKey(EnergyTypeID))
            {
                entityGenDB.EnergyStoreMax[EnergyTypeID] += MaxStore;
            }
            else
            {
                entityGenDB.EnergyStored[EnergyTypeID] = 0;
                entityGenDB.EnergyStoreMax[EnergyTypeID] = MaxStore;
            }
        }
    }

    public class EntityEnergyGenAbilityDB : BaseDataBlob
    {
        public DateTime dateTimeLastProcess;
        public ICargoable EnergyType;
        public double TotalOutputMax = 0;

        public (Guid type, double maxUse) TotalFuelUseAtMax;

        public double Demand { get; private set; }
        
        //as a percentage of max output. 
        public double Load { get; internal set; }

        public double Output { get; internal set; }
        public void AddDemand(double demand, DateTime atDateTime)
        {
            EnergyGenProcessor.EnergyGen(OwningEntity, atDateTime);
            Demand += demand;
        }

        public Dictionary<Guid, double > EnergyStored = new Dictionary<Guid, double>();
        public Dictionary<Guid, double > EnergyStoreMax = new Dictionary<Guid, double>();
        
        public double LocalFuel;

        private int _histogramSize = 60;
        public int HistogramSize
        {
            get { return _histogramSize;}
            set
            {
                if (_histogramSize > value)
                {
                    Histogram.RemoveRange(value, _histogramSize - value);
                    //Histogram.TrimExcess();
                    Histogram.Capacity = value;
                }
                else
                {
                    Histogram.Capacity = value;
                }
                _histogramSize = value;
            }
        }
    
        public int HistogramIndex = 0;
        public DateTime HistogramStartDate;
        public List<(double outputval, double demandval, double storval, int seconds)> Histogram = new List<(double, double, double, int)>(60);

        [JsonConstructor]
        private EntityEnergyGenAbilityDB()
        {
        }

        public EntityEnergyGenAbilityDB(DateTime gameTime)
        {
            HistogramStartDate = gameTime;
            Random rng = new Random();
            for (int i = 0; i < _histogramSize; i++)
            {
                double o = rng.Next(0, 50);
                double d = rng.Next(0, 50);
                double s = rng.Next(0, 50);
                int lastt = 0;
                if(i > 0)     
                    lastt = Histogram[i - 1].seconds;
                int t = rng.Next(lastt, lastt + 60);
                Histogram.Add((o,d,s,t));
            }
        }

        public EntityEnergyGenAbilityDB(EntityEnergyGenAbilityDB db)
        {
            Histogram = new List<(double outputval, double demandval, double storval, int seconds)>(db.Histogram);
            _histogramSize = db._histogramSize;
            HistogramStartDate = db.HistogramStartDate;
            dateTimeLastProcess = db.dateTimeLastProcess;
            EnergyType = db.EnergyType;
            EnergyStored = new Dictionary<Guid, double>(db.EnergyStored);
            EnergyStoreMax = new Dictionary<Guid, double>(db.EnergyStoreMax);
            TotalOutputMax = db.TotalOutputMax;
            TotalFuelUseAtMax = db.TotalFuelUseAtMax;
            Demand = db.Demand;
            Load = db.Load;
        }

        public override object Clone()
        {
            throw new NotImplementedException();
        }
    }

    public class EnergyGenProcessor : IInstanceProcessor
    {
        
        public static void EnergyGen(Entity entity, DateTime atDateTime)
        {
            EntityEnergyGenAbilityDB _energyGenDB = entity.GetDataBlob<EntityEnergyGenAbilityDB>();

            TimeSpan t = atDateTime - _energyGenDB.dateTimeLastProcess; 
            
            Guid energyType = _energyGenDB.EnergyType.ID;
            var stored = _energyGenDB.EnergyStored[energyType];
            var storeMax = _energyGenDB.EnergyStoreMax[energyType];
            double freestore = Math.Max(0, storeMax - stored);
            
            double totaldemand = _energyGenDB.Demand + freestore;
            
            var output = _energyGenDB.TotalOutputMax - _energyGenDB.Demand;
            
            output = GMath.Clamp(output, -stored, freestore);
            _energyGenDB.EnergyStored[energyType] += output;

            if (output > 0)
            {
                double timeToFill = freestore / output;
                DateTime interuptTime = atDateTime + TimeSpan.FromSeconds(timeToFill);
                entity.Manager.ManagerSubpulses.AddEntityInterupt(interuptTime, nameof(EnergyGenProcessor), entity);
            }
            else if (output < 0)
            {
                double timeToEmpty = Math.Abs(stored / output);
                DateTime interuptTime = atDateTime + TimeSpan.FromSeconds(timeToEmpty);
                entity.Manager.ManagerSubpulses.AddEntityInterupt(interuptTime, nameof(EnergyGenProcessor), entity);
            }


            double load = 0;
            if (output > 0)
            {
                load = _energyGenDB.TotalOutputMax / output;
            }
            else if (output < 0)
            {
                load = 1;
            }
            _energyGenDB.Load = load;
            _energyGenDB.Output = output;
            double fueluse = _energyGenDB.TotalFuelUseAtMax.maxUse * load;
            _energyGenDB.LocalFuel -= fueluse * t.TotalSeconds;
            
            _energyGenDB.dateTimeLastProcess = atDateTime;

            var histogram = _energyGenDB.Histogram;
            int hgFirstIdx = _energyGenDB.HistogramIndex;
            int hgLastIdx;
            if (hgFirstIdx == 0)
                hgLastIdx = histogram.Count - 1;
            else
                hgLastIdx = hgFirstIdx - 1;
            
            var hgFirstObj = histogram[hgFirstIdx];
            var hgLastObj = histogram[hgLastIdx];
            int optime = hgLastObj.seconds;

            int newoptime = (int)(optime + t.TotalSeconds);
            
            var nexval = (foo: output, demand: totaldemand, store: stored, newoptime);
            
            if(histogram.Count < _energyGenDB.HistogramSize)
                histogram.Add(nexval);
            else
            {
                histogram[hgFirstIdx] = nexval;
                if (hgFirstIdx == histogram.Count - 1)
                    _energyGenDB.HistogramIndex = 0;
                else
                {
                    _energyGenDB.HistogramIndex++;
                }
            }
            
            

        }


        internal override void ProcessEntity(Entity entity, DateTime atDateTime)
        {
            EnergyGen(entity, atDateTime);
        }
    }
}