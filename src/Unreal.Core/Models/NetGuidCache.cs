﻿using System;
using System.Collections.Generic;
using Unreal.Core.Extensions;

namespace Unreal.Core.Models
{
    /// <summary>
    /// Class to track all NetGuids being loaded during a replay.
    /// </summary>
    public partial class NetGuidCache
    {
        public NetGuidCache()
        {
        }

        //public Dictionary<uint, NetGuidCacheObject> ObjectLookup { get; private set; } = new Dictionary<uint, NetGuidCacheObject>();

        /// <summary>
        /// Maps net field export group name to the respective FNetFieldExportGroup
        /// </summary>
        public Dictionary<string, NetFieldExportGroup> NetFieldExportGroupMap { get; private set; } = new Dictionary<string, NetFieldExportGroup>();

        /// <summary>
        /// Maps assigned net field export group index to the respective FNetFieldExportGroup name.
        /// </summary>
        public Dictionary<uint, string> NetFieldExportGroupIndexToGroup { get; private set; } = new Dictionary<uint, string>();

        /// <summary>
        /// Maps netguid to the respective FNetFieldExportGroup name.
        /// </summary>
        public Dictionary<uint, string> NetGuidToPathName { get; private set; } = new Dictionary<uint, string>();

        /// <summary>
        /// Maps assigned net field export group index to the respective FNetFieldExportGroup name.
        /// </summary>
        public Dictionary<uint, NetFieldExportGroup> NetFieldExportGroupMapPathFixed { get; private set; } = new Dictionary<uint, NetFieldExportGroup>();

        /// <summary>
        /// Holds data about the tag dictionary
        /// </summary>
        public NetFieldExportGroup NetworkGameplayTagNodeIndex
        {
            get
            {
                if (_networkGameplayTagNodeIndex == null)
                {
                    if (NetFieldExportGroupMap.TryGetValue("NetworkGameplayTagNodeIndex", out var nodeIndex))
                    {
                        _networkGameplayTagNodeIndex = nodeIndex;
                    }
                }
                return _networkGameplayTagNodeIndex;
            }
        }

        private Dictionary<uint, NetFieldExportGroup> _archTypeToExportGroup = new Dictionary<uint, NetFieldExportGroup>();
        private Dictionary<uint, string> _cleanedPaths = new Dictionary<uint, string>();
        private Dictionary<string, string> _cleanedClassNetCache = new Dictionary<string, string>();
        private HashSet<string> _failedPaths = new HashSet<string>(); //Path names that didn't find an export group
        private NetFieldExportGroup _networkGameplayTagNodeIndex { get; set; }

        /// <summary>
        /// Add a <see cref="NetFieldExportGroup"/> to the GuidCache.
        /// </summary>
        public void AddToExportGroupMap(string group, NetFieldExportGroup exportGroup)
        {
            if (group.EndsWith("ClassNetCache"))
            {
                exportGroup.PathName = exportGroup.PathName.RemoveAllPathPrefixes();
            }

            NetFieldExportGroupMap[group] = exportGroup;
            NetFieldExportGroupIndexToGroup[exportGroup.PathNameIndex] = group;
        }

        /// <summary>
        /// Get the <see cref="NetFieldExportGroup"/> by the index.
        /// </summary>
        /// <param name="index"></param>
        /// <returns><see cref="NetFieldExportGroup"/></returns>
        public NetFieldExportGroup GetNetFieldExportGroupFromIndex(uint index)
        {
            if (!NetFieldExportGroupIndexToGroup.TryGetValue(index, out var group))
            {
                return null;
            }
            return NetFieldExportGroupMap[group];
        }

        /// <summary>
        /// Get the <see cref="NetFieldExportGroup"/> by the path.
        /// </summary>
        /// <param name="path"></param>
        /// <returns><see cref="NetFieldExportGroup"/></returns>
        public NetFieldExportGroup GetNetFieldExportGroup(string path)
        {
            if (path == null || !NetFieldExportGroupMap.TryGetValue(path, out var group))
            {
                return null;
            }
            return group;
        }

        /// <summary>
        /// Get the <see cref="NetFieldExportGroup"/> by the Actor guid.
        /// </summary>
        /// <param name="netguid"></param>
        /// <returns><see cref="NetFieldExportGroup"/></returns>
        public NetFieldExportGroup GetNetFieldExportGroup(uint netguid)
        {
            if (!_archTypeToExportGroup.TryGetValue(netguid, out var group))
            {
                if (!NetGuidToPathName.TryGetValue(netguid, out var path))
                {
                    return null;
                }

                // Don't need to recheck. Some export groups are added later though
                if (_failedPaths.Contains(path))
                {
                    return null;
                }

                if (NetFieldExportGroupMapPathFixed.TryGetValue(netguid, out group))
                {
                    _archTypeToExportGroup[netguid] = NetFieldExportGroupMapPathFixed[netguid];
                    return group;
                }

                foreach (var groupPathKvp in NetFieldExportGroupMap)
                {
                    var groupPath = groupPathKvp.Key;

                    if (!_cleanedPaths.TryGetValue(groupPathKvp.Value.PathNameIndex, out var groupPathFixed))
                    {
                        groupPathFixed = groupPath.RemoveAllPathPrefixes();
                        _cleanedPaths[groupPathKvp.Value.PathNameIndex] = groupPathFixed;
                    }

                    if (path.Contains(groupPathFixed, StringComparison.Ordinal))
                    {
                        NetFieldExportGroupMapPathFixed[netguid] = NetFieldExportGroupMap[groupPath];
                        _archTypeToExportGroup[netguid] = NetFieldExportGroupMap[groupPath];

                        return NetFieldExportGroupMap[groupPath];
                    }
                }

                var cleanedPath = path.CleanPathSuffix();
                foreach (var groupPathKvp in NetFieldExportGroupMap)
                {
                    var groupPath = groupPathKvp.Key;

                    if (_cleanedPaths.TryGetValue(groupPathKvp.Value.PathNameIndex, out var groupPathFixed))
                    {
                        if (groupPathFixed.Contains(cleanedPath, StringComparison.Ordinal))
                        {
                            NetFieldExportGroupMapPathFixed[netguid] = NetFieldExportGroupMap[groupPath];
                            _archTypeToExportGroup[netguid] = NetFieldExportGroupMap[groupPath];

                            return NetFieldExportGroupMap[groupPath];
                        }
                    }
                }

                _failedPaths.Add(path);
                return null;
            }

            return group;
        }

        /// <summary>
        /// Tries to find the ClassNetCache for the given group path.
        /// </summary>
        /// <param name="group"></param>
        /// <returns>true if ClassNetCache was found, false otherwise</returns>
        public bool TryGetClassNetCache(string group, out NetFieldExportGroup netFieldExportGroup, bool useFullName)
        {
            if (group == null)
            {
                netFieldExportGroup = null;
                return false;
            }

            if (!_cleanedClassNetCache.TryGetValue(group, out var classNetCachePath))
            {
                classNetCachePath = useFullName ? $"{group}_ClassNetCache" : $"{group.RemoveAllPathPrefixes()}_ClassNetCache";
                _cleanedClassNetCache[group] = classNetCachePath;
            }

            return NetFieldExportGroupMap.TryGetValue(classNetCachePath, out netFieldExportGroup);
        }

        /// <summary>
        /// Tries to resolve the netguid using the <see cref="NetGuidToPathName"/>
        /// </summary>
        /// <param name="netguid"></param>
        /// <param name="pathName"></param>
        /// <returns>true if netguid was resolved, false otherwise</returns>
        public bool TryGetPathName(uint netguid, out string pathName)
        {
            return NetGuidToPathName.TryGetValue(netguid, out pathName);
        }

        /// <summary>
        /// Tries to resolve the tagIndex using the <see cref="NetworkGameplayTagNodeIndex"/>
        /// </summary>
        /// <param name="tagIndex"></param>
        /// <param name="tagName"></param>
        /// <returns>true if tag was resolved, false otherwise</returns>
        public bool TryGetTagName(uint tagIndex, out string tagName)
        {
            tagName = "";
            if (tagIndex < NetworkGameplayTagNodeIndex.NetFieldExportsLength)
            {
                if (NetworkGameplayTagNodeIndex.NetFieldExports[tagIndex] != null)
                {
                    tagName = NetworkGameplayTagNodeIndex.NetFieldExports[tagIndex]?.Name;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Empty the NetGuidCache
        /// </summary>
        public void Cleanup()
        {
            NetFieldExportGroupIndexToGroup.Clear();
            NetFieldExportGroupMap.Clear();
            NetGuidToPathName.Clear();
            //ObjectLookup.Clear();
            NetFieldExportGroupMapPathFixed.Clear();
            _networkGameplayTagNodeIndex = null;

            _archTypeToExportGroup.Clear();
            _cleanedPaths.Clear();
            _cleanedClassNetCache.Clear();
            _failedPaths.Clear();
        }
    }
}