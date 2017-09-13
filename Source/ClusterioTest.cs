using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Reflection;

using SimpleJSON;

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.Networking;

using KSP;
//using KSP.IO;
using KSP.UI.Screens;

namespace ClusterioTest
{
    //[KSPAddon(KSPAddon.Startup.Instantly, true)]
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    //[KSPScenario(ScenarioCreationOptions.AddToNewCareerGames | ScenarioCreationOptions.AddToExistingCareerGames, GameScenes.SPACECENTER)]
    public class ClusterioTest : MonoBehaviour
    {
        public static readonly String MOD_PATH = "GameData/ClusterioTest/";
        public static readonly String RESOURCE_PATH = "ClusterioTest/Resource/";

        private ApplicationLauncherButton stockToolbarClusterioButton = null;

        private bool clusterioWindowVisible = false;

        private Rect clusterioWindowRect;

        private Dictionary<string, int> clusterioInventory;
        private Dictionary<string, int> latestLaunchCosts;

        private Dictionary<string, bool> relevantItems;

        private string masterIP = "localhost";
        private string masterPort = "8080";

        private float fundsPerLDS = 100f;
        private float fuelPerRocketFuel = 100f;
        private float sciencePerSciencePack = 10f;

        private float lastInventoryUpdate = 0f;

        private bool debug = false;

        // TODO
        //
        // - Make the contents of the toolbar window sensitive on context
        // --- It should always show the contents of the Clusterio inventory, along with a button to refresh it
        // --- (the inventory should autorefresh every 10 seconds, if the player is in the space center, track station, or an editor)
        // --- In an editor, the window should also show the cost of the current vessel
        // --- Lastly, the window should also show current science, how many science packs can be transferred into Clusterio, and a button to transfer the science
        // - Make the launchpad window (VesselSpawnDialog) actually work
        //
        // LONGER TERM
        //
        // - Introduce more complex costs (Reaction wheels and RCS costs rocket control units, batteries cost Accumulators, solar panels cost Factorio solar panels, etc.)

        public void Awake()
        {
            Debug.Log("ClusterioTest: Awake");

            DontDestroyOnLoad(this);

            GameEvents.onGameSceneSwitchRequested.Add(OnGameSceneSwitchRequested);
            GameEvents.onGUILaunchScreenSpawn.Add(OnGUILaunchScreenSpawn);
            GameEvents.onLevelWasLoadedGUIReady.Add(OnLevelWasLoadedGUIReady);
            GameEvents.onEditorShipModified.Add(OnEditorShipModified);
            GameEvents.onGUIApplicationLauncherReady.Add(OnGUIAppLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(OnGUIApplicationLauncherDestroyed);
            GameEvents.onVesselRecovered.Add(OnVesselRecovered);

            StreamReader reader = new StreamReader(MOD_PATH + "config.json");

            if (reader != null)
            {
                JSONNode modConfig = JSON.Parse(reader.ReadToEnd());

                if (modConfig["masterIP"] != null)
                {
                    Debug.Log(String.Format("ClusterioTest: masterIP is {0}", modConfig["masterIP"]));
                    masterIP = modConfig["masterIP"];

                    if (masterIP.ToLowerInvariant() != "localhost")
                    {
                        masterIP = "http://" + masterIP;
                    }
                }

                if (modConfig["masterPort"] != null)
                {
                    Debug.Log(String.Format("ClusterioTest: masterPort is {0}", modConfig["masterPort"]));
                    masterPort = modConfig["masterPort"];
                }

                if (modConfig["fundsPerLDS"] != null)
                {
                    Debug.Log(String.Format("ClusterioTest: fundsPerLDS is {0}", modConfig["fundsPerLDS"]));
                    fundsPerLDS = modConfig["fundsPerLDS"];
                }

                if (modConfig["fuelPerRocketFuel"] != null)
                {
                    Debug.Log(String.Format("ClusterioTest: fuelPerRocketFuel is {0}", modConfig["fuelPerRocketFuel"]));
                    fuelPerRocketFuel = modConfig["fuelPerRocketFuel"];
                }

                if (modConfig["sciencePerSciencePack"] != null)
                {
                    Debug.Log(String.Format("ClusterioTest: sciencePerSciencePack is {0}", modConfig["sciencePerSciencePack"]));
                    sciencePerSciencePack = modConfig["sciencePerSciencePack"];
                }

                if (modConfig["debug"] != null)
                {
                    Debug.Log(String.Format("ClusterioTest: sciencePerSciencePack is {0}", modConfig["sciencePerSciencePack"]));
                    debug = modConfig["debug"];
                }
            }

            clusterioWindowRect = new Rect(Screen.width / 2 - 150, Screen.height / 2 - 150, 300, 100);

            clusterioInventory = new Dictionary<string, int>();
            latestLaunchCosts = new Dictionary<string, int>();

            relevantItems = new Dictionary<string, bool>();
            relevantItems["low-density-structure"] = true;
            relevantItems["rocket-fuel"] = true;
            relevantItems["space-science-pack"] = true;
        }

        /*public void Start()
        {
            Debug.Log("ClusterioTest: Start");
        }*/

        private void OnGameSceneSwitchRequested(GameEvents.FromToAction<GameScenes, GameScenes> sceneSwitch)
        {
            if (stockToolbarClusterioButton != null)
            {
                stockToolbarClusterioButton.SetFalse();
            }

            // If the player has reverted to VAB or SPH, refund the spent items
            if (sceneSwitch.from == GameScenes.FLIGHT && sceneSwitch.to == GameScenes.EDITOR)
            {
                foreach (KeyValuePair<string, int> kvPair in latestLaunchCosts)
                {
                    StartCoroutine(AddItemsToClusterio(kvPair.Key, kvPair.Value));
                }
            }
        }

        private void OnGUILaunchScreenSpawn(GameEvents.VesselSpawnInfo e)
        {
            Debug.Log("ClusterioTest: onGUILaunchScreenSpawn");

            //onGUILaunchScreenSpawn is called before VesselSpawnDialog's Start() happens, but we want to wait after it's done so that we can replace
            StartCoroutine(ReplaceLaunchButtonFunction());
        }

        IEnumerator ReplaceLaunchButtonFunction()
        {
            yield return new WaitForSeconds(.1f);

            if (VesselSpawnDialog.Instance != null)
            {
                Debug.Log("ClusterioTest: VesselSpawnDialog instance exists");

                // A bit of reflection because buttonLaunch in VesselSpawnDialog is private :/
                FieldInfo buttonFieldInfo = typeof(VesselSpawnDialog).GetField("buttonLaunch", BindingFlags.NonPublic | BindingFlags.Instance);

                if (buttonFieldInfo == null)
                {
                    Debug.Log("ClusterioTest: fieldInfo doesn't exist!");

                    yield break;
                }

                Button button = buttonFieldInfo.GetValue(VesselSpawnDialog.Instance) as Button;

                if (button == null)
                {
                    Debug.Log("ClusterioTest: reflected buttonLaunch doesn't exist!");

                    yield break;
                }

                button.onClick.RemoveAllListeners();
                //button.onClick.AddListener(new UnityAction(ClusterioPreflightResourceCheckAction)); //originalAction: VesselSpawnDialog.Instance.ButtonLaunch
                button.onClick.AddListener(new UnityAction(ApologiseForInconvenience)); //originalAction: VesselSpawnDialog.Instance.ButtonLaunch
            }
        }

        private void ApologiseForInconvenience()
        {
            MultiOptionDialog dialog = new MultiOptionDialog("InconvenienceApology", "Sorry, this mod does not support launching from this facility yet. Please launch from the VAB or SPH only", "Cannot launch", UISkinManager.GetSkin("KSP window 7"),
                    new DialogGUIBase[]
                    {
                        new DialogGUIButton("Unable to Launch", null)
                    });

            PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), dialog, false, null);
        }

        private void OnLevelWasLoadedGUIReady(GameScenes gameScene)
        {
            Debug.Log("ClusterioTest: onLevelWasLoadedGUIReady");

            if (gameScene == GameScenes.EDITOR)
            {
                EditorLogic.fetch.launchBtn.onClick.RemoveAllListeners();
                EditorLogic.fetch.launchBtn.onClick.AddListener(new UnityAction(ClusterioPreflightResourceCheckAction)); // originalAction: EditorLogic.fetch.launchVessel
            }
        }

        private void OnEditorShipModified(ShipConstruct shipConstruct)
        {
            CalculateLaunchCosts(ref latestLaunchCosts);
        }

        private void OnVesselRecovered(ProtoVessel recoveredVessel, bool quick)
        {
            Dictionary<string, int> recoveredResources = new Dictionary<string, int>();

            CalculateLaunchCosts(ref recoveredResources, recoveredVessel);

            if (recoveredResources.Count > 0)
            {
                string message = "Following resources have been recovered: \n";

                foreach (KeyValuePair<string, int> kvPair in recoveredResources)
                {
                    message += String.Format("\n {0} {1}", kvPair.Value, kvPair.Key);

                    StartCoroutine(AddItemsToClusterio(kvPair.Key, kvPair.Value));
                }

                message += "\n";

                MultiOptionDialog dialog = new MultiOptionDialog("ClusterioResourceRecovery", message, "Recovery successful", UISkinManager.GetSkin("KSP window 7"),
                    new DialogGUIBase[]
                    {
                        new DialogGUIButton("Continue", null)
                    });

                PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), dialog, false, null);
            }
        }

        private void ClusterioPreflightResourceCheckAction()
        {
            StartCoroutine(ClusterioPreflightResourceCheck());
        }

        IEnumerator ClusterioPreflightResourceCheck()
        {
            Debug.Log("ClusterioTest: PreflightResourceCheck");

            yield return StartCoroutine(GetClusterioInventory());

            Debug.Log("ClusterioTest: calculating launch costs");

            CalculateLaunchCosts(ref latestLaunchCosts);

            bool pass = true;

            foreach (KeyValuePair<string, int> kvPair in latestLaunchCosts)
            {
                if (!clusterioInventory.ContainsKey(kvPair.Key) || clusterioInventory[kvPair.Key] < kvPair.Value)
                {
                    pass = false;
                    break;
                }
            }

            if (!pass)
            { // Insufficient resources to launch
                string message = "You do not have enough resources to launch this vessel! This vessel needs: \n";

                foreach (KeyValuePair<string, int> kvPair in latestLaunchCosts)
                {
                    message += String.Format("\n {0} {1} (you have {2})", kvPair.Value, kvPair.Key, (clusterioInventory.ContainsKey(kvPair.Key)) ? clusterioInventory[kvPair.Key] : 0);
                }

                message += "\n";

                MultiOptionDialog dialog = new MultiOptionDialog("InsufficientClusterioResources", message, "Insufficient resources!", UISkinManager.GetSkin("KSP window 7"),
                    new DialogGUIBase[]
                    {
                        new DialogGUIButton("Unable to Launch", null)
                    });

                PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), dialog, false, null);
            }
            else
            { // Launch can proceed
                string message = "Launching this vessel will cost you: \n";

                foreach (KeyValuePair<string, int> kvPair in latestLaunchCosts)
                {
                    message += String.Format("\n {0} {1} (you have {2})", kvPair.Value, kvPair.Key, (clusterioInventory.ContainsKey(kvPair.Key)) ? clusterioInventory[kvPair.Key] : 0);
                }

                message += "\n";

                MultiOptionDialog dialog = new MultiOptionDialog("ClusterioLaunchConfirmation", message, "Launch Possible", UISkinManager.GetSkin("KSP window 7"),
                    new DialogGUIBase[]
                    {
                        new DialogGUIButton("Launch", new Callback(ProceedToLaunch)),
                        new DialogGUIButton("Cancel", null)
                    });

                PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), dialog, false, null);
            }
        }

        private void CalculateLaunchCosts(ref Dictionary<string, int> launchCosts, ProtoVessel protoVessel = null)
        {
            launchCosts.Clear();

            float shipCost = 0f;
            float fuelAmount = 0f;

            // ProtoVessel (for recovery)
            if (protoVessel != null)
            {
                foreach (ProtoPartSnapshot part in protoVessel.protoPartSnapshots)
                {
                    AvailablePart partInfo = part.partInfo;

                    shipCost += partInfo.cost + part.moduleCosts;

                    foreach (ProtoPartResourceSnapshot resource in part.resources)
                    {
                        PartResourceDefinition resourceInfo = resource.definition;

                        shipCost -= (float)(resourceInfo.unitCost * resource.maxAmount);

                        // Only add to fuel if it's one of the four main propellants
                        if (resource.resourceName == "LiquidFuel" || resource.resourceName == "Oxidizer" || resource.resourceName == "SolidFuel" || resource.resourceName == "MonoPropellant")
                        {
                            fuelAmount += (float)resource.amount;
                        }
                    }
                }
            }
            // ShipConstruct (for Editor)
            else if (HighLogic.LoadedScene == GameScenes.EDITOR)
            {
                ShipConstruct ship = EditorLogic.fetch.ship;

                if (ship == null)
                {
                    return;
                }

                foreach (Part part in ship.parts)
                {
                    AvailablePart partInfo = part.partInfo;

                    shipCost += partInfo.cost + part.GetModuleCosts(partInfo.cost, ModifierStagingSituation.CURRENT);

                    foreach (PartResource resource in part.Resources)
                    {
                        PartResourceDefinition resourceInfo = resource.info;

                        shipCost -= (float)(resourceInfo.unitCost * resource.maxAmount);

                        // Only add to fuel if it's one of the four main propellants
                        if (resource.resourceName == "LiquidFuel" || resource.resourceName == "Oxidizer" || resource.resourceName == "SolidFuel" || resource.resourceName == "MonoPropellant")
                        {
                            fuelAmount += (float)resource.amount;
                        }
                    }
                }
            }
            // ConfigNode (for launchpad and runway)
            else
            {
                if (VesselSpawnDialog.Instance != null)
                {
                    // TODO
                }
            }

            if (shipCost > 0)
            {
                launchCosts["low-density-structure"] = 1 + (int)((shipCost - (shipCost % fundsPerLDS)) / fundsPerLDS);
            }

            if (fuelAmount > 0)
            {
                launchCosts["rocket-fuel"] = 1 + (int)((fuelAmount - (fuelAmount % fuelPerRocketFuel)) / fuelPerRocketFuel);
            }
        }

        private void ProceedToLaunch()
        {
            if (HighLogic.LoadedScene == GameScenes.EDITOR)
            {
                EditorLogic.fetch.launchVessel();
                DeductLaunchPrice();
            }
            else
            {
                if (VesselSpawnDialog.Instance != null)
                {
                    VesselSpawnDialog.Instance.ButtonLaunch();
                    DeductLaunchPrice();
                }
            }
        }

        private void DeductLaunchPrice()
        {
            foreach (KeyValuePair<string, int> kvPair in latestLaunchCosts)
            {
                StartCoroutine(RemoveItemsFromClusterio(kvPair.Key, kvPair.Value));
            }
        }

        /*private void ShowDialog()
        {
            MultiOptionDialog dialog = new MultiOptionDialog("InsufficientClusterioResources", "Message", "Title!", UISkinManager.GetSkin("KSP window 7"),
                new DialogGUIBase[] 
                {
                    new DialogGUIButton("OptionText", new Callback(this.Cancel))
                });

            PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), dialog, true, null, true, string.Empty);

            InputLockManager.SetControlLock(ControlTypes.KSC_ALL, "launchSiteFacility");
        }

        private void Cancel()
        {
            InputLockManager.RemoveControlLock("launchSiteFacility");
        }*/

        /*private void CreateToolbarButton()
        {
            if (ApplicationLauncher.Instance != null && ApplicationLauncher.Ready)
            {
                Debug.Log("ClusterioTest: ApplicationLauncher is ready");
                OnGUIAppLauncherReady();
            }
            else
            {
                Debug.Log("ClusterioTest: ApplicationLauncher is not ready");
                GameEvents.onGUIApplicationLauncherReady.Add(OnGUIAppLauncherReady);
            }
        }*/

        private void OnGUIAppLauncherReady()
        {
            if (ApplicationLauncher.Ready && stockToolbarClusterioButton == null)
            {
                Debug.Log("ClusterioTest: creating stock toolbar button");

                stockToolbarClusterioButton = ApplicationLauncher.Instance.AddModApplication(
                    OnToolbarClusterioButtonOn,
                    OnToolbarClusterioButtonOff,
                    null,
                    null,
                    null,
                    null,
                    ApplicationLauncher.AppScenes.SPACECENTER | ApplicationLauncher.AppScenes.VAB | ApplicationLauncher.AppScenes.SPH | ApplicationLauncher.AppScenes.TRACKSTATION | ApplicationLauncher.AppScenes.FLIGHT | ApplicationLauncher.AppScenes.MAPVIEW,
                    (Texture)GameDatabase.Instance.GetTexture(RESOURCE_PATH + "icon_clusterio", false));

                if (stockToolbarClusterioButton == null) Debug.Log("ClusterioTest: could not register stock toolbar button!");
            }
        }

        private void OnGUIApplicationLauncherDestroyed()
        {
            if (stockToolbarClusterioButton != null)
            {
                Debug.Log("ClusterioTest: destroying stock toolbar button");

                ApplicationLauncher.Instance.RemoveModApplication(stockToolbarClusterioButton);
                stockToolbarClusterioButton = null;
            }
        }

        void OnToolbarClusterioButtonOn()
        {
            clusterioWindowVisible = true;
        }

        void OnToolbarClusterioButtonOff()
        {
            clusterioWindowVisible = false;
        }

        public void OnGUI()
        {
            if (clusterioWindowVisible)
            {
                clusterioWindowRect = GUILayout.Window(22347, clusterioWindowRect, OnClusterioWindowInternal, "Clusterio Interface");
            }
        }

        private void Update()
        {
            if (Time.unscaledTime > lastInventoryUpdate + 10)
            {
                lastInventoryUpdate = Time.unscaledTime;

                // Update Clusterio inventory periodically if not ingame
                if (HighLogic.LoadedScene == GameScenes.SPACECENTER || HighLogic.LoadedScene == GameScenes.EDITOR || HighLogic.LoadedScene == GameScenes.TRACKSTATION)
                {
                    StartCoroutine(GetClusterioInventory());
                }
            }
        }

        private void OnClusterioWindowInternal(int id)
        {
            GUILayout.BeginVertical();

            // Clusterio inventory handling
            if (HighLogic.LoadedScene == GameScenes.SPACECENTER || HighLogic.LoadedScene == GameScenes.EDITOR || HighLogic.LoadedScene == GameScenes.TRACKSTATION || HighLogic.LoadedScene == GameScenes.FLIGHT)
            {
                GUILayout.Label("Clusterio inventory:");

                if (clusterioInventory.Count > 0)
                {
                    /*foreach (KeyValuePair<string, int> kvPair in clusterioInventory)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(kvPair.Value.ToString());
                        GUILayout.Label(kvPair.Key);
                        GUILayout.EndHorizontal();
                    }*/

                    GUILayout.BeginHorizontal();

                    GUILayout.BeginVertical();
                    foreach (KeyValuePair<string, int> kvPair in clusterioInventory)
                    {
                        if (!relevantItems.ContainsKey(kvPair.Key)) { continue; }

                        GUILayout.Label(kvPair.Key + ":");
                    }
                    GUILayout.EndVertical();

                    GUILayout.BeginVertical();
                    foreach (KeyValuePair<string, int> kvPair in clusterioInventory)
                    {
                        if (!relevantItems.ContainsKey(kvPair.Key)) { continue; }

                        GUILayout.Label(kvPair.Value.ToString());
                    }
                    GUILayout.EndVertical();

                    GUILayout.EndHorizontal();
                }
                else
                {
                    GUILayout.Label("Clusterio inventory is empty.");
                }

                GUILayout.Space(8);

                if (GUILayout.Button("Refresh Clusterio inventory"))
                {
                    Debug.Log("ClusterioTest: refreshing Clusterio inventory");

                    StartCoroutine(GetClusterioInventory());
                }
            }

            // Show ship costs in editor
            if (HighLogic.LoadedScene == GameScenes.EDITOR)
            {
                GUILayout.Space(16);
                GUILayout.Label("", GUI.skin.horizontalSlider);
                GUILayout.Space(8);

                GUILayout.Label("Vessel cost:");

                if (latestLaunchCosts.Count > 0)
                {
                    GUILayout.BeginHorizontal();

                    GUILayout.BeginVertical();
                    foreach (KeyValuePair<string, int> kvPair in latestLaunchCosts)
                    {
                        GUILayout.Label(kvPair.Key + ":");
                    }
                    GUILayout.EndVertical();

                    GUILayout.BeginVertical();
                    foreach (KeyValuePair<string, int> kvPair in latestLaunchCosts)
                    {
                        GUILayout.Label(kvPair.Value.ToString());
                    }
                    GUILayout.EndVertical();

                    GUILayout.EndHorizontal();
                }
                else
                {
                    GUILayout.Label("None");
                }
            }

            // Science transfer to Factorio
            //if (HighLogic.LoadedScene == GameScenes.SPACECENTER || HighLogic.LoadedScene == GameScenes.EDITOR || HighLogic.LoadedScene == GameScenes.TRACKSTATION || HighLogic.LoadedScene == GameScenes.FLIGHT)
            if (HighLogic.LoadedScene == GameScenes.SPACECENTER || HighLogic.LoadedScene == GameScenes.TRACKSTATION)
            {
                GUILayout.Space(16);
                GUILayout.Label("", GUI.skin.horizontalSlider);
                GUILayout.Space(8);

                int scienceTransferAmount = (int)(ResearchAndDevelopment.Instance.Science - (ResearchAndDevelopment.Instance.Science % sciencePerSciencePack));

                GUILayout.Label(String.Format("Can use {0} science to transfer {1} science packs to Factorio.", scienceTransferAmount, (scienceTransferAmount / sciencePerSciencePack).ToString()));

                GUILayout.Space(8);

                if (GUILayout.Button("Transfer Science to Clusterio") && ResearchAndDevelopment.Instance != null)
                {
                    Debug.Log("ClusterioTest: transferring science to Factorio");

                    StartCoroutine(TransferScienceToClusterio());
                }
            }

            if (debug)
            {
                if (GUILayout.Button("Add 1000 low density structures"))
                {
                    Debug.Log("ClusterioTest: adding 1000 low density structures");

                    StartCoroutine(AddItemsToClusterio("low-density-structure", 1000));
                }

                if (GUILayout.Button("Add 1000 rocket fuel"))
                {
                    Debug.Log("ClusterioTest: adding 1000 rocket fuel");

                    StartCoroutine(AddItemsToClusterio("rocket-fuel", 1000));
                }
            }

            GUILayout.EndVertical();

            // ---
            GUI.DragWindow();
        }

        IEnumerator GetClusterioInventory()
        {
            string apiRequest = "/api/inventory";

            Debug.Log(String.Format("ClusterioTest: ##{0}##", masterIP + ":" + masterPort + apiRequest));

            UnityWebRequest webRequest = UnityWebRequest.Get(masterIP + ":" + masterPort + apiRequest);

            yield return webRequest.Send();

            if (webRequest.isError)
            {
                Debug.Log("ClusterioTest: " + webRequest.error);
            }
            else
            {
                // Inventory request successful, refresh the inventory
                clusterioInventory.Clear();

                Debug.Log("ClusterioTest: ClusterioInventoryFound");

                // Show results as text
                //Debug.Log("ClusterioTest: " + webRequest.downloadHandler.text);

                JSONNode rootNode = JSON.Parse(webRequest.downloadHandler.text);

                foreach (JSONNode childNode in rootNode.Children)
                {
                    if (childNode["name"] == null)
                    {
                        Debug.Log("ClusterioTest: an item is missing its name!");
                        continue;
                    }

                    if (childNode["count"] == null)
                    {
                        Debug.Log(String.Format("ClusterioTest: item {0} is missing its count number!", childNode["name"]));
                        continue;
                    }

                    clusterioInventory[childNode["name"]] = childNode["count"].AsInt;

                    Debug.Log(String.Format("ClusterioTest: inventory - {0} {1}", childNode["count"], childNode["name"]));
                }

                Debug.Log("ClusterioTest: Finished taking inventory");
            }
        }

        IEnumerator TransferScienceToClusterio()
        {
            string apiCommand = "/api/place";

            Dictionary<string, string> sendValues = new Dictionary<string, string>();

            sendValues["name"] = "space-science-pack";

            int scienceTransferAmount = (int)(ResearchAndDevelopment.Instance.Science - (ResearchAndDevelopment.Instance.Science % sciencePerSciencePack));

            sendValues["count"] = (scienceTransferAmount / sciencePerSciencePack).ToString();

            Debug.Log(String.Format("ClusterioTest: sending string {0}", sendValues.toJson()));

            UnityWebRequest webRequest = UnityWebRequest.Post(masterIP + ":" + masterPort + apiCommand, sendValues);

            yield return webRequest.Send();

            if (webRequest.isError)
            {
                Debug.Log("ClusterioTest: " + webRequest.error);
            }
            else
            {
                // Show results as text
                Debug.Log("ClusterioTest: webRequest placed successfully");

                // Only subtract science in KSP if the request successfuly reached the Clusterio master server
                ResearchAndDevelopment.Instance.AddScience(-scienceTransferAmount, TransactionReasons.ScienceTransmission);
            }
        }

        IEnumerator RemoveItemsFromClusterio(string itemName, int count)
        {
            // First, we need to see what items are stored in the Clusterio server, and wait for that request to be finished
            yield return StartCoroutine(GetClusterioInventory());

            // Don't do a remove request if there's not enough items
            if (clusterioInventory[itemName] < count)
            {
                yield break;
            }

            Dictionary<string, string> sendValues = new Dictionary<string, string>();

            sendValues["name"] = itemName;
            sendValues["count"] = count.ToString();

            Debug.Log(String.Format("ClusterioTest: sending string {0}", sendValues.toJson()));

            string apiCommand = "/api/remove";

            UnityWebRequest webRequest = UnityWebRequest.Post(masterIP + ":" + masterPort + apiCommand, sendValues);

            yield return webRequest.Send();

            if (webRequest.isError)
            {
                Debug.Log("ClusterioTest: " + webRequest.error);
            }
            else
            {
                // Show results as text
                Debug.Log("ClusterioTest: webRequest placed successfully");
            }
        }

        IEnumerator AddItemsToClusterio(string itemName, int count)
        {
            Dictionary<string, string> sendValues = new Dictionary<string, string>();

            sendValues["name"] = itemName;
            sendValues["count"] = count.ToString();

            Debug.Log(String.Format("ClusterioTest: sending string {0}", sendValues.toJson()));

            string apiCommand = "/api/place";

            UnityWebRequest webRequest = UnityWebRequest.Post(masterIP + ":" + masterPort + apiCommand, sendValues);

            yield return webRequest.Send();

            if (webRequest.isError)
            {
                Debug.Log("ClusterioTest: " + webRequest.error);
            }
            else
            {
                // Show results as text
                Debug.Log("ClusterioTest: webRequest placed successfully");
            }
        }
    }
}
