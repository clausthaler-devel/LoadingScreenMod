﻿using System;
using System.Collections.Generic;
using ColossalFramework.Packaging;
using ColossalFramework.Steamworks;
using UnityEngine;

namespace LoadingScreenMod
{
    internal sealed class UsedAssets
    {
        internal static UsedAssets instance;
        readonly static PackageDeserializer.CustomDeserializeHandler defaultHandler = PackageDeserializer.customDeserializer;
        HashSet<string> buildingPackages = new HashSet<string>(), propPackages = new HashSet<string>(), treePackages = new HashSet<string>(), vehiclePackages = new HashSet<string>();
        HashSet<string> buildingAssets = new HashSet<string>(), propAssets = new HashSet<string>(), treeAssets = new HashSet<string>(), vehicleAssets = new HashSet<string>();
        Package.Asset[] assets;
        internal HashSet<string> Buildings => buildingAssets;
        internal HashSet<string> Props => propAssets;
        internal HashSet<string> Trees => treeAssets;
        internal HashSet<string> Vehicles => vehicleAssets;

        internal UsedAssets()
        {
            instance = this;
        }

        internal void Setup()
        {
            LookupUsed();
            PackageDeserializer.SetCustomDeserializer(new PackageDeserializer.CustomDeserializeHandler(CustomDeserialize));
        }

        internal void LookupUsed()
        {
            LookupSimulationBuildings(buildingPackages, buildingAssets);
            LookupSimulationAssets<PropInfo>(propPackages, propAssets);
            LookupSimulationAssets<TreeInfo>(treePackages, treeAssets);
            LookupSimulationAssets<VehicleInfo>(vehiclePackages, vehicleAssets);
        }

        internal void Dispose()
        {
            PackageDeserializer.SetCustomDeserializer(defaultHandler);
            buildingPackages.Clear(); propPackages.Clear(); treePackages.Clear(); vehiclePackages.Clear(); buildingAssets.Clear(); propAssets.Clear(); treeAssets.Clear(); vehicleAssets.Clear();
            buildingPackages = null; propPackages = null; treePackages = null; vehiclePackages = null; buildingAssets = null; propAssets = null; treeAssets = null; vehicleAssets = null;
            instance = null; assets = null;
        }

        internal bool GotPropTreeTrailerPackage(string packageName)
        {
            // Some false positives are possible at this stage because of dots.
            return propPackages.Contains(packageName) || treePackages.Contains(packageName) || vehiclePackages.Contains(packageName) || packageName.IndexOf('.') >= 0;
        }

        internal bool GotBuildingVehiclePackage(string packageName)
        {
            // Some false positives are possible at this stage because of dots.
            return buildingPackages.Contains(packageName) || vehiclePackages.Contains(packageName) || packageName.IndexOf('.') >= 0;
        }

        internal bool GotProp(string fullName) => propAssets.Contains(fullName);
        internal bool GotTree(string fullName) => treeAssets.Contains(fullName);
        internal bool GotBuilding(string fullName) => buildingAssets.Contains(fullName);
        internal bool GotVehicle(string fullName) => vehicleAssets.Contains(fullName);

        internal void ReportMissingAssets()
        {
            ReportMissingAssets<BuildingInfo>(buildingAssets);
            ReportMissingAssets<PropInfo>(propAssets);
            ReportMissingAssets<TreeInfo>(treeAssets);
            ReportMissingAssets<VehicleInfo>(vehicleAssets);
        }

        static void ReportMissingAssets<P>(HashSet<string> customAssets) where P : PrefabInfo
        {
            try
            {
                foreach (string name in customAssets)
                    if (PrefabCollection<P>.FindLoaded(name) == null)
                        AssetLoader.instance.NotFound(name);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
            }
        }

        internal bool AnyMissing(HashSet<string> ignore)
        {
            return AnyMissing<BuildingInfo>(buildingAssets, ignore) || AnyMissing<PropInfo>(propAssets, ignore) ||
                   AnyMissing<TreeInfo>(treeAssets, ignore) || AnyMissing<VehicleInfo>(vehicleAssets, ignore);
        }

        static bool AnyMissing<P>(HashSet<string> fullNames, HashSet<string> ignore) where P : PrefabInfo
        {
            foreach (string name in fullNames)
                if (!ignore.Contains(name) && PrefabCollection<P>.FindLoaded(name) == null)
                    return true;

            return false;
        }

        /// <summary>
        /// Looks up the custom assets placed in the city.
        /// </summary>
        static void LookupSimulationAssets<P>(HashSet<string> packages, HashSet<string> assets) where P : PrefabInfo
        {
            try
            {
                int n = PrefabCollection<P>.PrefabCount();

                for (int i = 0; i < n; i++)
                    Add(PrefabCollection<P>.PrefabName((uint) i), packages, assets);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
            }
        }

        /// <summary>
        /// BuildingInfos require more effort because the NotUsedGuide/UnlockMilestone stuff gets into way.
        /// </summary>
        static void LookupSimulationBuildings(HashSet<string> packages, HashSet<string> assets)
        {
            try
            {
                Building[] buffer = BuildingManager.instance.m_buildings.m_buffer;
                int n = buffer.Length;

                for (int i = 1; i < n; i++)
                    if (buffer[i].m_flags != Building.Flags.None)
                        Add(PrefabCollection<BuildingInfo>.PrefabName(buffer[i].m_infoIndex), packages, assets);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
            }
        }

        static void Add(string name, HashSet<string> packages, HashSet<string> assets)
        {
            int j;

            // Recognize custom assets:
            if (!string.IsNullOrEmpty(name) && (j = name.IndexOf('.')) >= 0 && j < name.Length - 1)
            {
                packages.Add(name.Substring(0, j)); // packagename (or pac in case the full name is pac.kagename.assetname)
                assets.Add(name); // packagename.assetname
            }
        }

        static object CustomDeserialize(Package p, Type t, PackageReader r)
        {
            // First, make the common case fast.
            if (t == typeof(float))
                return r.ReadSingle();
            if (t == typeof(Vector2))
                return r.ReadVector2();

            // Props and trees in buildings and parks.
            if (t == typeof(BuildingInfo.Prop))
            {
                string propName = r.ReadString();
                string treeName = r.ReadString();
                PropInfo pi = PrefabCollection<PropInfo>.FindLoaded(propName);
                TreeInfo ti =  PrefabCollection<TreeInfo>.FindLoaded(treeName);

                if (pi == null && !string.IsNullOrEmpty(propName) && LoadPropTree(ref propName))
                    pi = PrefabCollection<PropInfo>.FindLoaded(propName);

                if (ti == null && !string.IsNullOrEmpty(treeName) && LoadPropTree(ref treeName))
                    ti = PrefabCollection<TreeInfo>.FindLoaded(treeName);

                return new BuildingInfo.Prop
                {
                    m_prop = pi,
                    m_tree = ti,
                    m_position = r.ReadVector3(),
                    m_angle = r.ReadSingle(),
                    m_probability = r.ReadInt32(),
                    m_fixedHeight = r.ReadBoolean()
                };
            }

            // It seems that trailers are listed in the save game so this is not necessary. Better to be safe however
            // because a missing trailer reference is fatal for the simulation thread.
            if (t == typeof(VehicleInfo.VehicleTrailer))
            {
                string name = r.ReadString();
                string trailerName = p.packageName + "." + name;
                VehicleInfo vi = PrefabCollection<VehicleInfo>.FindLoaded(trailerName);

                if (vi == null && LoadTrailer(p, name))
                    vi = PrefabCollection<VehicleInfo>.FindLoaded(trailerName);

                VehicleInfo.VehicleTrailer trailer;
                trailer.m_info = vi;
                trailer.m_probability = r.ReadInt32();
                trailer.m_invertProbability = r.ReadInt32();
                return trailer;
            }

            return defaultHandler(p, t, r);
        }

        /// <summary>
        /// Given packagename.assetname, find the asset. Unfortunately this is a bit more complicated than expected because dots are possible everywhere.
        /// Even PackageManager.FindAssetByName() does it wrong.
        /// </summary>
        static Package.Asset FindAsset(string name)
        {
            try
            {
                int j = name.IndexOf('.');

                if (j >= 0 && j < name.Length - 1)
                {
                    Package package; Package.Asset asset;
                    ulong id;

                    // The fast path: it is a workshop asset.
                    if (ulong.TryParse(name.Substring(0, j), out id) && (package = PackageManager.FindPackageBy(new PublishedFileId(id))) != null &&
                        (asset = package.Find(name.Substring(j + 1))) != null)
                            return asset;
                }

                Package.Asset[] a = UsedAssets.instance.assets;

                if (a == null)
                    a = UsedAssets.instance.assets = AssetLoader.FilterAssets(Package.AssetType.Object);

                // We also try the old (early 2015) naming that does not contain the package name. FindLoaded does it, too.
                for (int i = 0; i < a.Length; i++)
                    if (name == a[i].fullName || name == a[i].name)
                        return a[i];
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
            }

            return null;
        }

        static bool LoadPropTree(ref string fullName)
        {
            Package.Asset data = FindAsset(fullName);

            if (data != null)
                try
                {
                    AssetLoader.instance.PropTreeTrailerImpl(data.package, data);
                    fullName = data.fullName;
                    return true;
                }
                catch (Exception e)
                {
                    AssetLoader.instance.Failed(data, e);
                }
            else
                AssetLoader.instance.NotFound(fullName);

            return false;
        }

        static bool LoadTrailer(Package package, string name)
        {
            Package.Asset data = package.Find(name);

            if (data != null)
                try
                {
                    AssetLoader.instance.PropTreeTrailerImpl(package, data);
                    return true;
                }
                catch (Exception e)
                {
                    AssetLoader.instance.Failed(data, e);
                }
            else
                AssetLoader.instance.NotFound(package.packageName + "." + name);

            return false;
        }
    }
}
