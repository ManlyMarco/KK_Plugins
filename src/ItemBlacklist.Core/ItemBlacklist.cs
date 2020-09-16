﻿using BepInEx;
using BepInEx.Logging;
using ChaCustom;
using HarmonyLib;
using Sideloader.AutoResolver;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KK_Plugins
{
    [BepInPlugin(GUID, PluginName, Version)]
    [BepInDependency(Sideloader.Sideloader.GUID, "16.0")]
    public partial class ItemBlacklist : BaseUnityPlugin
    {
        public const string GUID = "com.deathweasel.bepinex.itemblacklist";
        public const string PluginName = "Item Blacklist";
        public const string PluginNameInternal = "KK_ItemBlacklist";
        public const string Version = "1.0";
        internal static new ManualLogSource Logger;

        private static CustomSelectListCtrl CustomSelectListCtrlInstance;
        private static CustomSelectInfoComponent CurrentCustomSelectInfoComponent;
        private static readonly string BlacklistDirectory = Path.Combine(UserData.Path, "save");
        private static readonly string BlacklistFile = Path.Combine(BlacklistDirectory, "itemblacklist.xml");
        //GUID/Category/ID
        private static readonly Dictionary<string, Dictionary<int, HashSet<int>>> Blacklist = new Dictionary<string, Dictionary<int, HashSet<int>>>();
        private static readonly Dictionary<CustomSelectListCtrl, ListVisibilityType> ListVisibility = new Dictionary<CustomSelectListCtrl, ListVisibilityType>();

        private static bool MouseIn = false;

        internal void Main()
        {
            Logger = base.Logger;

            Harmony.CreateAndPatchAll(typeof(Hooks));
            SceneManager.sceneLoaded += (s, lsm) => SetMenuVisibility(false);

            LoadBlacklist();
        }

        internal void Update()
        {
            if (Input.GetMouseButtonDown(1))
                ShowMenu();
        }

        private static bool CheckBlacklist(string guid, int category, int id)
        {
            if (Blacklist.TryGetValue(guid, out var x))
                if (x.TryGetValue(category, out var y))
                    if (y.Contains(id))
                        return true;
            return false;
        }

        private void LoadBlacklist()
        {
            Directory.CreateDirectory(BlacklistDirectory);

            XDocument blacklistXML;
            if (File.Exists(BlacklistFile))
            {
                blacklistXML = XDocument.Load(BlacklistFile);
            }
            else
            {
                blacklistXML = new XDocument();
                blacklistXML.Add(new XElement("itemBlacklist"));
                blacklistXML.Save(BlacklistFile);
            }

            var itemBlacklist = blacklistXML.Element("itemBlacklist");
            foreach (var modElement in itemBlacklist.Elements("mod"))
            {
                string guid = modElement.Attribute("guid")?.Value;
                if (!guid.IsNullOrEmpty())
                {
                    foreach (var categoryElement in modElement.Elements("category"))
                    {
                        if (int.TryParse(categoryElement.Attribute("number")?.Value, out int category))
                        {
                            foreach (var itemElement in categoryElement.Elements("item"))
                            {
                                if (int.TryParse(itemElement.Attribute("id")?.Value, out int id))
                                {
                                    if (!Blacklist.ContainsKey(guid))
                                        Blacklist[guid] = new Dictionary<int, HashSet<int>>();
                                    if (!Blacklist[guid].ContainsKey(category))
                                        Blacklist[guid][category] = new HashSet<int>();
                                    Blacklist[guid][category].Add(id);
                                }
                            }
                        }
                    }
                }
            }
        }

        private void SaveBlacklist()
        {
            File.Delete(BlacklistFile);

            XDocument blacklistXML = new XDocument();
            XElement itemBlacklistElement = new XElement("itemBlacklist");
            blacklistXML.Add(itemBlacklistElement);

            foreach (var x in Blacklist)
            {
                XElement modElement = new XElement("mod");
                modElement.SetAttributeValue("guid", x.Key);
                itemBlacklistElement.Add(modElement);

                foreach (var y in x.Value)
                {
                    XElement categoryElement = new XElement("category");
                    categoryElement.SetAttributeValue("number", y.Key);
                    modElement.Add(categoryElement);

                    foreach (var z in y.Value)
                    {
                        XElement itemElement = new XElement("item");
                        itemElement.SetAttributeValue("id", z);
                        categoryElement.Add(itemElement);
                    }
                }
            }

            blacklistXML.Save(BlacklistFile);
        }

        public void ChangeListFilter(ListVisibilityType visibilityType) => ChangeListFilter(CustomSelectListCtrlInstance, visibilityType);

        public static void ChangeListFilter(CustomSelectListCtrl customSelectListCtrl, ListVisibilityType visibilityType)
        {
            List<CustomSelectInfo> lstSelectInfo = (List<CustomSelectInfo>)Traverse.Create(customSelectListCtrl).Field("lstSelectInfo").GetValue();

            int count = 0;
            foreach (CustomSelectInfo customSelectInfo in lstSelectInfo)
            {
                if (visibilityType == ListVisibilityType.All)
                {
                    customSelectListCtrl.DisvisibleItem(customSelectInfo.index, false);
                    continue;
                }

                bool hide = visibilityType != ListVisibilityType.Filtered;

                if (customSelectInfo.index >= UniversalAutoResolver.BaseSlotID)
                {
                    ResolveInfo Info = UniversalAutoResolver.TryGetResolutionInfo((ChaListDefine.CategoryNo)customSelectInfo.category, customSelectInfo.index);
                    if (Info != null)
                    {
                        if (CheckBlacklist(Info.GUID, (int)Info.CategoryNo, Info.Slot))
                        {
                            hide = visibilityType == ListVisibilityType.Filtered;
                            count++;
                        }
                    }
                }
                customSelectListCtrl.DisvisibleItem(customSelectInfo.index, hide);
            }
            ListVisibility[customSelectListCtrl] = visibilityType;

            if (count == 0 && visibilityType == ListVisibilityType.Hidden)
            {
                Logger.LogMessage($"No items are hidden");
                ChangeListFilter(customSelectListCtrl, ListVisibilityType.Filtered);
            }
        }

        private void BlacklistItem(string guid, int category, int id, int index)
        {
            if (!Blacklist.ContainsKey(guid))
                Blacklist[guid] = new Dictionary<int, HashSet<int>>();
            if (!Blacklist[guid].ContainsKey(category))
                Blacklist[guid][category] = new HashSet<int>();
            Blacklist[guid][category].Add(id);
            SaveBlacklist();

            foreach (var customSelectListCtrl in CustomBase.Instance.GetComponentsInChildren<CustomSelectListCtrl>(true))
                if (customSelectListCtrl.GetSelectInfoFromIndex(index)?.category == category)
                    if (ListVisibility.TryGetValue(customSelectListCtrl, out var visibilityType))
                        if (visibilityType == ListVisibilityType.Filtered)
                            customSelectListCtrl.DisvisibleItem(index, true);

            SetMenuVisibility(false);
        }
        private void UnblacklistItem(string guid, int category, int id, int index)
        {
            if (!Blacklist.ContainsKey(guid))
                Blacklist[guid] = new Dictionary<int, HashSet<int>>();
            if (!Blacklist[guid].ContainsKey(category))
                Blacklist[guid][category] = new HashSet<int>();
            Blacklist[guid][category].Remove(id);
            SaveBlacklist();

            bool changeFilter = false;
            foreach (var customSelectListCtrl in CustomBase.Instance.GetComponentsInChildren<CustomSelectListCtrl>(true))
            {
                if (customSelectListCtrl.GetSelectInfoFromIndex(index)?.category == category)
                {
                    if (ListVisibility.TryGetValue(customSelectListCtrl, out var visibilityType))
                        if (visibilityType == ListVisibilityType.Hidden)
                            customSelectListCtrl.DisvisibleItem(index, true);

                    List<CustomSelectInfo> lstSelectInfo = (List<CustomSelectInfo>)Traverse.Create(customSelectListCtrl).Field("lstSelectInfo").GetValue();

                    if (lstSelectInfo.All(x => x.disvisible))
                        changeFilter = true;
                }
            }

            if (changeFilter)
                ChangeListFilter(ListVisibilityType.Filtered);
            SetMenuVisibility(false);
        }

        private void BlacklistMod(string guid)
        {
            List<CustomSelectInfo> lstSelectInfo = (List<CustomSelectInfo>)Traverse.Create(CustomSelectListCtrlInstance).Field("lstSelectInfo").GetValue();

            foreach (CustomSelectInfo customSelectInfo in lstSelectInfo)
            {
                if (customSelectInfo.index >= UniversalAutoResolver.BaseSlotID)
                {
                    ResolveInfo Info = UniversalAutoResolver.TryGetResolutionInfo((ChaListDefine.CategoryNo)customSelectInfo.category, customSelectInfo.index);
                    if (Info != null && Info.GUID == guid)
                    {
                        if (!Blacklist.ContainsKey(Info.GUID))
                            Blacklist[Info.GUID] = new Dictionary<int, HashSet<int>>();
                        if (!Blacklist[Info.GUID].ContainsKey(customSelectInfo.category))
                            Blacklist[Info.GUID][customSelectInfo.category] = new HashSet<int>();
                        Blacklist[Info.GUID][customSelectInfo.category].Add(Info.Slot);
                        SaveBlacklist();

                        foreach (var customSelectListCtrl in CustomBase.Instance.GetComponentsInChildren<CustomSelectListCtrl>(true))
                            if (customSelectListCtrl.GetSelectInfoFromIndex(customSelectInfo.index)?.category == customSelectInfo.category)
                                if (ListVisibility.TryGetValue(customSelectListCtrl, out var visibilityType))
                                    if (visibilityType == ListVisibilityType.Filtered)
                                        customSelectListCtrl.DisvisibleItem(customSelectInfo.index, true);
                    }
                }
            }

            SetMenuVisibility(false);
        }
        private void UnblacklistMod(string guid)
        {
            List<CustomSelectInfo> lstSelectInfo = (List<CustomSelectInfo>)Traverse.Create(CustomSelectListCtrlInstance).Field("lstSelectInfo").GetValue();

            bool changeFilter = false;
            foreach (CustomSelectInfo customSelectInfo in lstSelectInfo)
            {
                if (customSelectInfo.index >= UniversalAutoResolver.BaseSlotID)
                {
                    ResolveInfo Info = UniversalAutoResolver.TryGetResolutionInfo((ChaListDefine.CategoryNo)customSelectInfo.category, customSelectInfo.index);
                    if (Info != null && Info.GUID == guid)
                    {
                        if (!Blacklist.ContainsKey(Info.GUID))
                            Blacklist[Info.GUID] = new Dictionary<int, HashSet<int>>();
                        if (!Blacklist[Info.GUID].ContainsKey(customSelectInfo.category))
                            Blacklist[Info.GUID][customSelectInfo.category] = new HashSet<int>();
                        Blacklist[Info.GUID][customSelectInfo.category].Remove(Info.Slot);
                        SaveBlacklist();

                        foreach (var customSelectListCtrl in CustomBase.Instance.GetComponentsInChildren<CustomSelectListCtrl>(true))
                            if (customSelectListCtrl.GetSelectInfoFromIndex(customSelectInfo.index)?.category == customSelectInfo.category)
                            {
                                if (ListVisibility.TryGetValue(customSelectListCtrl, out var visibilityType))
                                    if (visibilityType == ListVisibilityType.Hidden)
                                        customSelectListCtrl.DisvisibleItem(customSelectInfo.index, true);

                                List<CustomSelectInfo> lstSelectInfo2 = (List<CustomSelectInfo>)Traverse.Create(customSelectListCtrl).Field("lstSelectInfo").GetValue();

                                if (lstSelectInfo2.All(x => x.disvisible))
                                    changeFilter = true;
                            }
                    }
                }
            }

            if (changeFilter)
                ChangeListFilter(ListVisibilityType.Filtered);
            SetMenuVisibility(false);
        }

        private void PrintInfo(int index)
        {
            List<CustomSelectInfo> lstSelectInfo = (List<CustomSelectInfo>)Traverse.Create(CustomSelectListCtrlInstance).Field("lstSelectInfo").GetValue();
            var customSelectInfo = lstSelectInfo.FirstOrDefault(x => x.index == index);

            if (index >= UniversalAutoResolver.BaseSlotID)
            {
                ResolveInfo Info = UniversalAutoResolver.TryGetResolutionInfo((ChaListDefine.CategoryNo)customSelectInfo.category, customSelectInfo.index);
                if (Info != null)
                {
                    Logger.LogMessage($"Item GUID:{Info.GUID} Category:{(int)Info.CategoryNo}({Info.CategoryNo}) ID:{Info.Slot}");
                    if (Sideloader.Sideloader.ZipArchives.TryGetValue(Info.GUID, out string zipFileName))
                        Logger.LogMessage($"Zip File:{Path.GetFileName(zipFileName)}");
                }
            }
            else
            {
                Logger.LogMessage($"Item Category:{CurrentCustomSelectInfoComponent.info.category}({(ChaListDefine.CategoryNo)CurrentCustomSelectInfoComponent.info.category}) ID:{CurrentCustomSelectInfoComponent.info.index}");
            }
            SetMenuVisibility(false);
        }

        public enum ListVisibilityType { Filtered, Hidden, All }
    }
}