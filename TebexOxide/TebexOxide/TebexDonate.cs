using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;

using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Tebex Donate", "Tebex", "1.7.0")]
    [Description("Official support for the Tebex server monetization platform")]
    public class TebexDonate : CovalencePlugin
    {
        #region Classes and Structures

        private class Category : SubCategory
        {
            public SortedDictionary<int, SubCategory> SubCategories { get; }

            public Category(int id, string name, int parentId, SortedDictionary<int, Package> packages, SortedDictionary<int, SubCategory> subCategories) : base(id, name, parentId, packages)
            {
                SubCategories = subCategories;
            }
        }

        private struct Command
        {
            public int Id { get; }
            public bool Online { get; }
            public string PlayerId { get; }
            public string PlayerName { get; }
            public string CommandString { get; }

            public Command(int id, bool online, string playerId, string playerName, string commandString)
            {
                Id = id;
                Online = online;
                PlayerId = playerId;
                PlayerName = playerName;
                CommandString = commandString;
            }

            public string ReplaceVariables() => CommandString.Replace("{id}", PlayerId).Replace("{username}", PlayerName);
        }

        [JsonObject]
        private class Event
        {
            [JsonProperty("username_id")]
            public string UsernameId { get; }
            [JsonProperty("event_type")]
            public string EventType { get; }
            [JsonProperty("event_date")]
            public string EventDate { get; }
            [JsonProperty("ip")]
            public string IpAddress { get; }

            public Event(string usernameId, string eventType, string eventDate, string ipAddress)
            {
                UsernameId = usernameId;
                EventType = eventType;
                EventDate = eventDate;
                IpAddress = ipAddress;
            }

            public override string ToString() => $"{{\"username_id\": \"{UsernameId}\", \"event_type\": \"{EventType}\", \"event_date\": \"{EventDate}\", \"ip\": \"{IpAddress}\"}}";
        }

        private class Package
        {
            public int Id { get; }
            public string Name { get; }
            public string Price { get; }
            public bool Image { get; }
            public string ImageUrl { get; }

            public Package(int id, string name, string price, bool image, string imageUrl)
            {
                Id = id;
                Name = name;
                Price = price;
                Image = image;
                ImageUrl = imageUrl;
            }
        }

        private class SubCategory
        {
            public int Id { get; }
            public string Name { get; }
            public int ParentId { get; }
            public SortedDictionary<int, Package> Packages { get; }

            public SubCategory(int id, string name, int parentId, SortedDictionary<int, Package> packages)
            {
                Id = id;
                Name = name;
                ParentId = parentId;
                Packages = packages;
            }
        }

#if RUST
        private class RustUIBuilder
        {
            public string Panel { get; }
            private Oxide.Game.Rust.Cui.CuiElementContainer container;

            public RustUIBuilder(string panel, string colour, string anchorMin, string anchorMax)
            {
                Panel = panel;
                container = CreateElementContainer(panel, colour, anchorMin, anchorMax);
            }

            public RustUIBuilder AddPanel(string panel, string colour, string anchorMin, string anchorMax, bool cursor)
            {
                container.Add(new Oxide.Game.Rust.Cui.CuiPanel()
                {
                    Image = { Color = colour },
                    RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax },
                    CursorEnabled = cursor
                }, panel, Oxide.Game.Rust.Cui.CuiHelper.GetGuid());
                return this;
            }

            public RustUIBuilder AddButton(string panel, string colour, string text, int size, string anchorMin, string anchorMax, string command, TextAnchor align)
            {
                container.Add(new Oxide.Game.Rust.Cui.CuiButton()
                {
                    Button = { Color = colour, Command = command },
                    RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax },
                    Text = { Text = text, FontSize = size, Align = align }
                }, panel, Oxide.Game.Rust.Cui.CuiHelper.GetGuid());
                return this;
            }

            public RustUIBuilder AddImage(string panel, string url, string anchorMin, string anchorMax)
            {
                container.Add(new Oxide.Game.Rust.Cui.CuiElement()
                {
                    Name = Oxide.Game.Rust.Cui.CuiHelper.GetGuid(),
                    Parent = panel,
                    Components =
                    {
                        new Oxide.Game.Rust.Cui.CuiRawImageComponent() { Url = url },
                        new Oxide.Game.Rust.Cui.CuiRectTransformComponent() { AnchorMin = anchorMin, AnchorMax = anchorMax }
                    }
                });
                return this;
            }

            public RustUIBuilder AddLabel(string panel, string text, int size, string anchorMin, string anchorMax, TextAnchor align)
            {
                container.Add(new Oxide.Game.Rust.Cui.CuiLabel()
                {
                    Text = { FontSize = size, Align = align, Text = text },
                    RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax }
                }, panel, Oxide.Game.Rust.Cui.CuiHelper.GetGuid());
                return this;
            }

            public Oxide.Game.Rust.Cui.CuiElementContainer GetContainer() => container;

            private Oxide.Game.Rust.Cui.CuiElementContainer CreateElementContainer(string panel, string colour, string anchorMin, string anchorMax)
            {
                return new Oxide.Game.Rust.Cui.CuiElementContainer()
                {
                    {
                        new Oxide.Game.Rust.Cui.CuiPanel()
                        {
                            Image = { Color = colour },
                            RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax },
                            CursorEnabled = true
                        },
                        new Oxide.Game.Rust.Cui.CuiElement().Parent = "Overlay", panel
                    }
                };
            }
        }

        private class RustUIManager
        {
            private static Dictionary<string, Oxide.Game.Rust.Cui.CuiElementContainer> availableUIs = new Dictionary<string, Oxide.Game.Rust.Cui.CuiElementContainer>();
            private static Dictionary<ulong, string> openUIs = new Dictionary<ulong, string>();

            public static void ClearUIs()
            {
                foreach (BasePlayer player in BasePlayer.activePlayerList)
                    CloseUI(player);

                availableUIs.Clear();
                openUIs.Clear();
            }

            public static void CloseUI(BasePlayer player)
            {
                string element;

                if (openUIs.TryGetValue(player.userID, out element))
                {
                    Oxide.Game.Rust.Cui.CuiHelper.DestroyUi(player, element);
                    openUIs.Remove(player.userID);
                }
            }

            public static void GenerateUIs(string storeName, string storeCurrency, string storeCurrencySymbol, SortedDictionary<int, Category> orderedCategories)
            {
                RustUIBuilder baseUIBuilder = GenerateBaseUI("TD_Listings", storeName, storeCurrency, null, "TD_Close");
                double anchorMinX = 0.01;
                double anchorMaxX = 0.10;
                double anchorIncrement = 0.095;

                foreach (KeyValuePair<int, Category> orderedCategory in orderedCategories)
                {
                    Category category = orderedCategory.Value;
                    RustUIBuilder categoryUIBuilder = GenerateBaseUI($"TD_Listings_{category.Id}", storeName, storeCurrency, category.Name, $"TD_Open {baseUIBuilder.Panel}");
                    double categoryAnchorMinX = 0.01;
                    double categoryAnchorMaxX = 0.10;

                    foreach (KeyValuePair<int, SubCategory> orderedSubCategory in category.SubCategories)
                    {
                        SubCategory subCategory = orderedSubCategory.Value;
                        RustUIBuilder subCategoryUIBuilder = GenerateBaseUI($"TD_Listings_{subCategory.Id}", storeName, storeCurrency, $"{category.Name} -> {subCategory.Name}", $"TD_Open {categoryUIBuilder.Panel}");
                        subCategoryUIBuilder = subCategoryUIBuilder.AddPanel(subCategoryUIBuilder.Panel, HexToRust(Instance.buyUIButtonColour, Instance.buyUIButtonColourTransparency), "0.01 0.06", "0.99 0.86", true);
                        subCategoryUIBuilder = AddPackages(subCategoryUIBuilder, storeCurrencySymbol, subCategory.Packages);

                        categoryUIBuilder = categoryUIBuilder.AddButton(categoryUIBuilder.Panel, HexToRust(Instance.buyUIButtonColour, Instance.buyUIButtonColourTransparency), subCategory.Name, 14, $"{categoryAnchorMinX} 0.88", $"{categoryAnchorMaxX} 0.93", $"TD_Open TD_Listings_{subCategory.Id}", TextAnchor.MiddleCenter);
                        categoryAnchorMinX += anchorIncrement; 
                        categoryAnchorMaxX += anchorIncrement;

                        availableUIs.Add(subCategoryUIBuilder.Panel, subCategoryUIBuilder.GetContainer());
                    }

                    if (category.Packages.Count > 0)
                    {
                        categoryUIBuilder = categoryUIBuilder.AddPanel(categoryUIBuilder.Panel, HexToRust(Instance.buyUIButtonColour, Instance.buyUIButtonColourTransparency), "0.01 0.06", "0.99 0.86", true);
                        categoryUIBuilder = AddPackages(categoryUIBuilder, storeCurrencySymbol, category.Packages);
                    }
                    else
                        categoryUIBuilder = categoryUIBuilder.AddLabel(categoryUIBuilder.Panel, Instance.lang.GetMessage("BuyUISelectSubCategory", Instance), 18, "0.01 0.02", "0.99 0.86", TextAnchor.MiddleCenter);

                    baseUIBuilder = baseUIBuilder.AddButton(baseUIBuilder.Panel, HexToRust(Instance.buyUIButtonColour, Instance.buyUIButtonColourTransparency), category.Name, 14, $"{anchorMinX} 0.88", $"{anchorMaxX} 0.93", $"TD_Open TD_Listings_{category.Id}", TextAnchor.MiddleCenter);
                    anchorMinX += anchorIncrement;
                    anchorMaxX += anchorIncrement;

                    availableUIs.Add(categoryUIBuilder.Panel, categoryUIBuilder.GetContainer());
                }

                baseUIBuilder = baseUIBuilder.AddLabel(baseUIBuilder.Panel, Instance.lang.GetMessage("BuyUISelectCategory", Instance), 18, "0.01 0.02", "0.99 0.86", TextAnchor.MiddleCenter);
                availableUIs.Add(baseUIBuilder.Panel, baseUIBuilder.GetContainer());
            }

            public static void OpenUI(BasePlayer player, string element)
            {
                Oxide.Game.Rust.Cui.CuiElementContainer container;

                if (!availableUIs.TryGetValue(element, out container))
                    return;

                string openElement;

                if (openUIs.TryGetValue(player.userID, out openElement))
                    Oxide.Game.Rust.Cui.CuiHelper.DestroyUi(player, openElement);

                openUIs[player.userID] = element;
                Oxide.Game.Rust.Cui.CuiHelper.AddUi(player, container.ToJson());
            }

            private static RustUIBuilder AddPackages(RustUIBuilder builder, string storeCurrencySymbol, SortedDictionary<int, Package> orderedPackages)
            {
                int currentPackage = 0;
                double packageAnchorMinX = 0.02;
                double packageAnchorMinY = 0.67;
                double packageAnchorMaxX = 0.13;
                double packageAnchorMaxY = 0.85;
                double packageAnchorXIncrement = 0.1215;
                double packageAnchorYDecrement = 0.2;

                foreach (KeyValuePair<int, Package> orderedPackage in orderedPackages)
                {
                    Package package = orderedPackage.Value;
                    string imageIcon = "";

                    if (package.Image)
                        imageIcon = Instance.ImageLibrary?.Call<string>("GetImage", package.ImageUrl, 0uL);

                    if (string.IsNullOrEmpty(imageIcon))
                        imageIcon = Instance.ImageLibrary?.Call<string>("GetImage", Instance.buyUIDefaultPackageImageUrl, 0uL);

                    builder = builder.AddPanel(builder.Panel, HexToRust(Instance.buyUIBackgroundColour, Instance.buyUIBackgroundColourTransparency), $"{packageAnchorMinX} {packageAnchorMinY}", $"{packageAnchorMaxX} {packageAnchorMaxY}", true)
                        .AddLabel(builder.Panel, package.Name, 12, $"{packageAnchorMinX} {packageAnchorMaxY - 0.05}", $"{packageAnchorMaxX} {packageAnchorMaxY}", TextAnchor.MiddleCenter)
                        .AddImage(builder.Panel, imageIcon, $"{packageAnchorMinX + 0.005} {packageAnchorMinY + 0.01}", $"{packageAnchorMaxX - ((packageAnchorMaxX - packageAnchorMinX) / 2) - 0.005} {packageAnchorMaxY - 0.055}")
                        .AddLabel(builder.Panel, $"{storeCurrencySymbol}{package.Price}", 12, $"{packageAnchorMaxX - ((packageAnchorMaxX - packageAnchorMinX) / 2) + 0.005} {packageAnchorMaxY - (packageAnchorYDecrement / 2)}", $"{packageAnchorMaxX - 0.005} {packageAnchorMaxY - 0.055}", TextAnchor.MiddleCenter)
                        .AddButton(builder.Panel, HexToRust(Instance.buyUIButtonColour, Instance.buyUIButtonColourTransparency), "Buy", 16, $"{packageAnchorMaxX - ((packageAnchorMaxX - packageAnchorMinX) / 2) + 0.005} {packageAnchorMinY + 0.01}", $"{packageAnchorMaxX - 0.005} {packageAnchorMaxY - (packageAnchorYDecrement / 2) - 0.01}", $"TD_Buy {package.Id}", TextAnchor.MiddleCenter);

                    currentPackage++;

                    if (currentPackage == 8)
                    {
                        currentPackage = 0;
                        packageAnchorMinX = 0.02;
                        packageAnchorMinY -= packageAnchorYDecrement;
                        packageAnchorMaxX = 0.13;
                        packageAnchorMaxY -= packageAnchorYDecrement;
                    }
                    else
                    {
                        packageAnchorMinX += packageAnchorXIncrement;
                        packageAnchorMaxX += packageAnchorXIncrement;
                    }
                }

                return builder;
            }

            private static RustUIBuilder GenerateBaseUI(string panelName, string storeName, string storeCurrency, string currentCategory, string closeCommand)
            {
                string header = $"{storeName}" + (currentCategory != null ? $" - {currentCategory}" : "");
                return new RustUIBuilder(panelName, HexToRust(Instance.buyUIBackgroundColour, Instance.buyUIBackgroundColourTransparency), "0.02 0.2", "0.98 0.9")
                    .AddLabel(panelName, header, 18, "0.01 0.94", "0.99 0.99", TextAnchor.MiddleCenter)
                    .AddButton(panelName, HexToRust(Instance.buyUIButtonExitColour, Instance.buyUIButtonExitColourTransparency), "Go Back", 18, "0.93 0.94", "0.99 0.99", closeCommand, TextAnchor.MiddleCenter);
            }

            private static string HexToRust(string hex, float alpha = 1f)
            {
                if (hex.StartsWith("#"))
                    hex = hex.TrimStart('#');

                int red = int.Parse(hex.Substring(0, 2), NumberStyles.AllowHexSpecifier);
                int green = int.Parse(hex.Substring(2, 2), NumberStyles.AllowHexSpecifier);
                int blue = int.Parse(hex.Substring(4, 2), NumberStyles.AllowHexSpecifier);

                return $"{(double)red / 255} {(double)green / 255} {(double)blue / 255} {alpha}";
            }
        }
#endif

        #endregion

        #region Constants and Variables

        public static TebexDonate Instance;
        private Queue<Event> events;

        #region API

        private static readonly string BASE_URL = "https://plugin.buycraft.net";
        private bool buyCommandReady;
        private SortedDictionary<int, Category> listings;
        private bool logEvents;
        private string storeCurrency;
        private string storeCurrencySymbol;
        private string storeName;
        private string storeUrl;
        private bool validated;

        #endregion

        #region Configuration

        private string buyCommand;
        private bool buyEnabled;

        #region UI

        private bool buyUIEnabled;
        private string buyUIBackgroundColour;
        private float buyUIBackgroundColourTransparency;
        private string buyUIButtonColour;
        private float buyUIButtonColourTransparency;
        private string buyUIButtonExitColour;
        private float buyUIButtonExitColourTransparency;
        private string buyUIDefaultPackageImageUrl;
        private ulong buyUIMessageAvatarId;

        #endregion

        private bool debugLogActions;
        private bool debugLogResponseErrors;
        private bool debugLogStackTraces;
        private string secretKey;

        #endregion

        #region Plugin References

        [PluginReference]
        private Plugin ImageLibrary;

        #endregion

        #region Timers

        private Timer checkTimer;
        private Timer eventTimer;
        private Timer validationTimer;

        #endregion

        #endregion

        #region Initialisation

        protected override void LoadDefaultConfig()
        {
            Config["Buy Command", "Alias"] = buyCommand = GetConfig("Buy Command", "Alias", "buy");
            Config["Buy Command", "Enabled"] = buyEnabled = GetConfig("Buy Command", "Enabled", true);
            Config["Buy Command", "UI", "Enabled"] = buyUIEnabled = GetConfig("Buy Command", "UI", "Enabled", true);
            Config["Buy Command", "UI", "Background Colour"] = buyUIBackgroundColour = GetConfig("Buy Command", "UI", "Background Colour", "#2a2a2a");
            Config["Buy Command", "UI", "Background Colour Transparency"] = buyUIBackgroundColourTransparency = GetConfig("Buy Command", "UI", "Background Colour Transparency", 0.9f);
            Config["Buy Command", "UI", "Button Colour"] = buyUIButtonColour = GetConfig("Buy Command", "UI", "Button Colour", "#a8a8a8");
            Config["Buy Command", "UI", "Button Colour Transparency"] = buyUIButtonColourTransparency = GetConfig("Buy Command", "UI", "Button Colour Transparency", 0.5f);
            Config["Buy Command", "UI", "Button Exit Colour"] = buyUIButtonExitColour = GetConfig("Buy Command", "UI", "Button Exit Colour", "#b31b1b");
            Config["Buy Command", "UI", "Button Exit Colour Transparency"] = buyUIButtonExitColourTransparency = GetConfig("Buy Command", "UI", "Button Exit Colour Transparency", 0.9f);
            Config["Buy Command", "UI", "Default Package Image URL"] = buyUIDefaultPackageImageUrl = GetConfig("Buy Command", "UI", "Default Package Image URL", "https://steamcdn-a.akamaihd.net/steamcommunity/public/images/avatars/b5/b5bd56c1aa4644a474a2e4972be27ef9e82e517e_full.jpg");
            Config["Buy Command", "UI", "Message Avatar ID"] = buyUIMessageAvatarId = GetConfig("Buy Command", "UI", "Message Avatar ID", 0uL);
            Config["Debug", "Log Actions"] = debugLogActions = GetConfig("Debug", "Log Actions", true);
            Config["Debug", "Log Stack Traces"] = debugLogStackTraces = GetConfig("Debug", "Log Stack Traces", true);
            Config["Debug", "Log Response Errors"] = debugLogResponseErrors = GetConfig("Debug", "Log Response Errors", true);
            Config["Secret key of your shop (do not tell it anyone)"] = secretKey = GetConfig("Secret key of your shop (do not tell it anyone)", "");

            SaveConfig();
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["BuyCheckoutURL"] = "Visit {url} to purchase this item!",
                ["BuyCommand"] = "You can buy packages from our store at {url}",
                ["BuyUISelectCategory"] = "Select a category to view our packages!",
                ["BuyUISelectSubCategory"] = "Select a sub-category to view our packages!"
            }, this);
        }

        private void OnServerInitialized()
        {
            Instance = this;
            events = new Queue<Event>();

            LoadDefaultConfig();
            LoadDefaultMessages();

            buyCommandReady = false;
            listings = new SortedDictionary<int, Category>();

            if (buyEnabled)
                AddCovalenceCommand(buyCommand, "BuyCommand");

            logEvents = false;
            validated = false;

            StartValidationTimer();

#if RUST
            ImageLibrary?.Call<bool>("AddImage", buyUIDefaultPackageImageUrl, buyUIDefaultPackageImageUrl, 0uL);
#endif
        }

        private void Unload()
        {
#if RUST
            if (!Interface.Oxide.IsShuttingDown)
                RustUIManager.ClearUIs();
#endif

            if (checkTimer != null && !checkTimer.Destroyed)
                checkTimer.Destroy();

            if (eventTimer != null && !eventTimer.Destroyed)
                eventTimer.Destroy();

            if (validationTimer != null && !validationTimer.Destroyed)
                validationTimer.Destroy();
        }

        #endregion

        #region Commands

        #region Player Commands

        private void BuyCommand(IPlayer player, string command, string[] args)
        {
            if (!buyCommandReady)
                return;

            if (player.IsServer)
            {
                Puts("You cannot use this command from the console window!");
                return;
            }

#if RUST
            if (buyUIEnabled)
                RustUIManager.OpenUI(player.Object as BasePlayer, "TD_Listings");
            else
#endif
            player.Message(lang.GetMessage("BuyCommand", this, player.Id).Replace("{url}", storeUrl));
        }

        #endregion

        #region Server Commands

        [Command("tebex:info")]
        private void TebexInfoCommand(IPlayer player, string command, string[] args)
        {
            if (!player.IsServer)
                return;

            FetchStoreInformation(true, secretKey);
        }

        [Command("tebex:secret")]
        private void TebexSecretCommand(IPlayer player, string command, string[] args)
        {
            if (!player.IsServer)
                return;

            if (args.Length != 1)
            {
                PrintWarning("Usage: tebex:secret <secret>");
                return;
            }

            FetchStoreInformation(true, args[0]);
        }

        #endregion

        #region UI Commands

#if RUST
        [Command("TD_Buy")]
        private void TebexUI_Buy(IPlayer player, string command, string[] args)
        {
            if (args.Length != 1)
                return;

            CheckoutURL(player, args[0]);
        }

        [Command("TD_Open")]
        private void TebexUI_Open(IPlayer player, string command, string[] args)
        {
            if (args.Length != 1)
                return;

            RustUIManager.OpenUI(player.Object as BasePlayer, args[0]);
        }

        [Command("TD_Close")]
        private void TebexUI_Close(IPlayer player, string command, string[] args) => RustUIManager.CloseUI(player.Object as BasePlayer);
#endif

        #endregion

        #endregion

        #region Oxide Hooks

#if RUST
        private object OnPlayerCommand(BasePlayer player, string command, string[] args)
        {
            string fullCommand = command.ToLower();

            if (!fullCommand.StartsWith("/"))
                return null;

            switch (fullCommand.Split(' ')[0])
            {
                case "/td_buy":
                    return true;
                case "/td_close":
                    return true;
                case "/td_open":
                    return true;
            }

            return null;
        }
#endif

        private void OnUserConnected(IPlayer player) => events.Enqueue(new Event(player.Id, "server.join", FormattedUtcDate(), player.Address));

        private void OnUserDisconnected(IPlayer player)
        {
#if RUST
            var basePlayer = player.Object as BasePlayer;

            if (basePlayer != null)
                RustUIManager.CloseUI(basePlayer);
#endif
            
        }

        #endregion

        #region Web Request Methods

        private void CheckCommandQueue(bool first)
        {
            if (validated)
            {
                if (first && validationTimer != null && !validationTimer.Destroyed)
                    validationTimer.Destroy();
            }
            else
                return;

            if (debugLogActions)
                PrintWarning("Attempting to process commands in the queue...");

            webrequest.Enqueue($"{BASE_URL}/queue", "", (code, response) =>
            {
                float secondsUntilNextCheck = 225f;

                if (response != null)
                {
                    try
                    {
                        var jObject = JObject.Parse(response);

                        switch (code)
                        {
                            case 200:
                                break;
                            case 403:
                                StartValidationTimer(60f);
                                goto default;
                            default:
                                PrintWarning($"An error occurred whilst checking the command queue: {jObject["error_message"].ToString()}");
                                return;
                        }

                        if ((bool) jObject["meta"]["execute_offline"])
                        {
                            if (debugLogActions)
                                PrintWarning("Processing offline commands...");

                            ProcessOfflineCommands();
                        }

                        JArray jObjectPlayers = (JArray) jObject["players"];
                        secondsUntilNextCheck = jObject["meta"]["next_check"].ToObject<int>();

                        if (jObjectPlayers.Count > 0)
                        {
                            int processed = 0;

                            foreach (var player in jObjectPlayers)
                            {
                                IPlayer target = players.FindPlayerById(player["uuid"].ToString());

                                if (target == null || !target.IsConnected)
                                    continue;

                                if (debugLogActions)
                                    PrintWarning($"Processing online commands for {target.Id}...");

                                ProcessOnlineCommands(target, player["id"].ToString());
                                processed++;
                            }

                            if (processed == 0 && debugLogActions)
                                PrintWarning("There are no online commands that need to be processed!");
                        }
                        else if (debugLogActions)
                            PrintWarning("There are no online commands that need to be processed!");
                    }
                    catch (Exception e)
                    {
                        PrintError($"An exception was thrown whilst checking the command queue: {e.Message}");

                        if (debugLogResponseErrors)
                            PrintError($"Associated response: {response}");

                        if (debugLogStackTraces)
                            PrintError(e.StackTrace);
                    }

                    checkTimer = timer.In(secondsUntilNextCheck, () => CheckCommandQueue(false));
                    return;
                }

                checkTimer = timer.In(secondsUntilNextCheck, () => CheckCommandQueue(false));
                PrintError($"An unhandled error occurred whilst checking the command queue (response code: {code}).");
            }, this, RequestMethod.GET, AddToHeaders(secretKey), 3000f);
        }

        private void CheckoutURL(IPlayer player, string packageId)
        {
            webrequest.Enqueue($"{BASE_URL}/checkout?username={player.Name}&package_id={packageId}", "", (code, response) =>
            {
                if (response != null)
                {
                    try
                    {
                        var jObject = JObject.Parse(response);

                        switch (code)
                        {
                            case 200:
                            case 201:
                                break;
                            case 403:
                                StartValidationTimer(60f);
                                goto default;
                            default:
                                PrintWarning($"An error occurred whilst retrieving a checkout URL: {jObject["error_message"].ToString()}");
                                return;
                        }

                        string message = lang.GetMessage("BuyCheckoutURL", this, player.Id).Replace("{url}", jObject["url"].ToString());

#if RUST
                        if ($"{buyUIMessageAvatarId}".IsSteamId())
                        {
                            var basePlayer = player.Object as BasePlayer;
                            basePlayer?.SendConsoleCommand("chat.add", 2, buyUIMessageAvatarId, message);
                        }
                        else
                            player.Message(message);

                        player.Command("TD_Close");
#else
                        player.Message(message);
#endif
                    }
                    catch (Exception e)
                    {
                        PrintError($"An exception was thrown whilst retrieving a checkout URL: {e.Message}");

                        if (debugLogResponseErrors)
                            PrintError($"Associated response: {response}");

                        if (debugLogStackTraces)
                            PrintError(e.StackTrace);
                    }

                    return;
                }

                PrintError($"An unhandled error occurred whilst retrieving a checkout URL (response code: {code}, username: {player.Name}, package ID: {packageId}).");
            }, this, RequestMethod.POST, AddToHeaders(secretKey), 3000f);
        }

        private void DeleteCommands(List<Command> commands)
        {
            if (commands.Count < 1)
                return;

            string commandIds = $"ids[]={commands[0].Id}";

            for (int i = 0; i < commands.Count; i++)
            {
                Command command = commands[i];

                if (debugLogActions)
                    PrintWarning($"Queueing command {command.Id} for deletion: {command.ReplaceVariables()}");

                if (i == 0)
                    continue;

                commandIds += $"&ids[]={command.Id}";
            }

            webrequest.Enqueue($"{BASE_URL}/queue?{commandIds}", "", (code, response) =>
            {
                switch (code)
                {
                    case 204:
                        if (debugLogActions)
                            PrintWarning("Successfully deleted all executed commands.");

                        return;
                    case 403:
                        StartValidationTimer(60f);
                        goto default;
                    default:
                        if (response != null)
                        {
                            try
                            {
                                var jObject = JObject.Parse(response);

                                PrintWarning($"An error occurred whilst deleting executed commands: {jObject["error_message"].ToString()}");
                                return;
                            }
                            catch (Exception e)
                            {
                                PrintError($"An exception was thrown whilst deleting executed commands: {e.Message}");

                                if (debugLogResponseErrors)
                                    PrintError($"Associated response: {response}");

                                if (debugLogStackTraces)
                                    PrintError(e.StackTrace);

                                return;
                            }
                        }

                        break;
                }

                PrintError($"An unhandled error occurred whilst deleting executed commands (response code: {code}).");
            }, this, RequestMethod.DELETE, AddToHeaders(secretKey), 3000f);
        }

        private void FetchListings()
        {
            buyCommandReady = false;
            listings.Clear();

            if (debugLogActions)
                PrintWarning("Attempting to fetch category and package listings...");

            webrequest.Enqueue($"{BASE_URL}/listing", "", (code, response) =>
            {
                if (response != null)
                {
                    try
                    {
                        var jObject = JObject.Parse(response);

                        switch (code)
                        {
                            case 200:
                                break;
                            case 403:
                                StartValidationTimer(60f);
                                goto default;
                            default:
                                PrintWarning($"An error occurred whilst fetching your store listings: {jObject["error_message"].ToString()}");
                                return;
                        }

                        JArray categories = (JArray) jObject["categories"];

                        if (categories.Count > 0)
                        {
                            foreach (var jCategory in categories)
                            {
                                Category category = new Category(jCategory["id"].ToObject<int>(), jCategory["name"].ToString(), -1, new SortedDictionary<int, Package>(), new SortedDictionary<int, SubCategory>());
                                JArray subCategories = (JArray)jCategory["subcategories"];
                                int order = jCategory["order"].ToObject<int>();

                                if (subCategories.Count > 0)
                                    foreach (var jSubCategory in subCategories)
                                    {
                                        SubCategory subCategory = new SubCategory(jSubCategory["id"].ToObject<int>(), jSubCategory["name"].ToString(), category.Id, new SortedDictionary<int, Package>());
                                        JArray subCategoryPackages = (JArray)jSubCategory["packages"];
                                        int subCategoryOrder = jSubCategory["order"].ToObject<int>();

                                        if (subCategoryPackages.Count > 0)
                                            foreach (var jSubCategoryPackage in subCategoryPackages)
                                            {
                                                bool image = jSubCategoryPackage["image"].Type != JTokenType.Boolean;
                                                Package package = new Package(jSubCategoryPackage["id"].ToObject<int>(), jSubCategoryPackage["name"].ToString(), jSubCategoryPackage["price"].ToString(), image, image ? null : jSubCategoryPackage["image"].ToString());

#if RUST
                                                if (package.Image)
                                                    ImageLibrary?.Call("AddImage", package.ImageUrl, package.ImageUrl, 0uL);
#endif

                                                int packageOrder = jSubCategoryPackage["order"].ToObject<int>();

                                                while (subCategory.Packages.ContainsKey(packageOrder))
                                                    packageOrder++;

                                                subCategory.Packages.Add(packageOrder, package);
                                            }

                                        while (category.SubCategories.ContainsKey(subCategoryOrder))
                                            subCategoryOrder++;

                                        category.SubCategories.Add(subCategoryOrder, subCategory);
                                    }

                                JArray packages = (JArray)jCategory["packages"];

                                if (packages.Count > 0)
                                    foreach (var jPackage in packages)
                                    {
                                        bool image = jPackage["image"].Type != JTokenType.Boolean;
                                        Package package = new Package(jPackage["id"].ToObject<int>(), jPackage["name"].ToString(), jPackage["price"].ToString(), image, image ? jPackage["image"].ToString() : null);
                                        int packageOrder = jPackage["order"].ToObject<int>();

                                        while (category.Packages.ContainsKey(packageOrder))
                                            packageOrder++;

                                        category.Packages.Add(packageOrder, package);
                                    }

                                while (listings.ContainsKey(order))
                                    order++;

                                listings.Add(order, category);

                                if (debugLogActions)
                                    PrintWarning($"Successfully added category {category.Name} to the stored listings!");
                            }

                            if (debugLogActions)
                                PrintWarning("Successfully added all categories to the stored listings!");
                        }
                        else if (debugLogActions)
                            PrintWarning("No categories or package listings were found on your store!");

#if RUST
                        RustUIManager.ClearUIs();
                        RustUIManager.GenerateUIs(storeName, storeCurrency, storeCurrencySymbol, listings);
#endif

                        buyCommandReady = true;
                    }
                    catch (Exception e)
                    {
                        PrintError($"An exception was thrown whilst fetching your store listings: {e.Message}");

                        if (debugLogResponseErrors)
                            PrintError($"Associated response: {response}");

                        if (debugLogStackTraces)
                            PrintError(e.StackTrace);
                    }

                    return;
                }

                PrintError($"An unhandled error occurred whilst fetching your store listings from the secret key provided (response code: {code}).");
            }, this, RequestMethod.GET, AddToHeaders(secretKey), 3000f);
        }

        private void FetchStoreInformation(bool command, string secretKey)
        {
            if (!command && !validated)
                validationTimer = timer.In(60f, () => FetchStoreInformation(false, this.secretKey));

            if (secretKey.Equals(""))
            {
                PrintWarning("You have not yet set your store's secret key in the configuration file!");
                PrintWarning("Use the \"tebex:secret <secret>\" command to update it without having to reload the plugin.");
                return;
            }

            webrequest.Enqueue($"{BASE_URL}/information", "", (code, response) =>
            {
                if (response != null)
                {
                    try
                    {
                        var jObject = JObject.Parse(response);

                        switch (code)
                        {
                            case 200:
                                break;
                            case 403:
                                StartValidationTimer(60f);
                                goto default;
                            default:
                                PrintWarning($"An error occurred whilst fetching your store information: {jObject["error_message"].ToString()}");
                                return;
                        }

                        storeCurrency = jObject["account"]["currency"]["iso_4217"].ToString();
                        storeCurrencySymbol = jObject["account"]["currency"]["symbol"].ToString();
                        storeName = jObject["account"]["name"].ToString();
                        storeUrl = jObject["account"]["domain"].ToString();

                        Puts("~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~");
                        Puts("Successfully retrieved store information from your secret key!");
                        Puts("~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~");
                        Puts($"Account: {storeName} ({storeCurrency})");
                        Puts($"Server: {jObject["server"]["name"].ToString()}");
                        Puts($"URL: {storeUrl}");
                        Puts("~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~");

                        if (!validated || !secretKey.Equals(this.secretKey))
                        {
                            validated = true;
                            this.secretKey = secretKey;

                            Config["Secret key of your shop (do not tell it anyone)"] = secretKey;
                            SaveConfig();

                            CheckCommandQueue(true);
                        }

                        logEvents = jObject["account"]["log_events"].ToObject<bool>();

                        if (logEvents)
                            StartEventTimer(60f);
                        else if (eventTimer != null && !eventTimer.Destroyed)
                            eventTimer.Destroy();

                        FetchListings();
                    }
                    catch (Exception e)
                    {
                        PrintError($"An exception was thrown whilst validating your secret key: {e.Message}");

                        if (debugLogResponseErrors)
                            PrintError($"Associated response: {response}");

                        if (debugLogStackTraces)
                            PrintError(e.StackTrace);
                    }

                    return;
                }

                PrintError($"An unhandled error occurred whilst fetching your store information from the secret key provided (response code: {code}).");
            }, this, RequestMethod.GET, AddToHeaders(secretKey), 3000f);
        }

        private void LogEvents()
        {
            if (!logEvents)
                return;

            if (eventTimer != null && !eventTimer.Destroyed)
                eventTimer.Destroy();

            Queue<Event> events = new Queue<Event>(this.events);
            eventTimer = timer.In(60f, () => LogEvents());

            if (events.Count < 1)
                return;

            Dictionary<string, string> headers = new Dictionary<string, string>()
            {
                ["Content-Type"] = "application/json"
            };
            Event nextEvent = events.Dequeue();
            StringBuilder payload = new StringBuilder("[").Append(nextEvent.ToString());

            while (events.Count > 0)
            {
                nextEvent = events.Dequeue();
                payload.Append(",").Append(nextEvent.ToString());
            }

            if (debugLogActions)
                PrintWarning("Attempting to log all stored connection events...");

            webrequest.Enqueue($"{BASE_URL}/events", payload.Append("]").ToString(), (code, response) =>
            {
                switch (code)
                {
                    case 204:
                        foreach (Event @event in events)
                            this.events.Dequeue();

                        if (debugLogActions)
                            PrintWarning("Successfully logged all stored connection events!");

                        return;
                    case 403:
                        StartValidationTimer(60f);
                        goto default;
                    default:
                        if (response != null)
                        {
                            try
                            {
                                var jObject = JObject.Parse(response);
                                PrintWarning($"An error occurred whilst logging player connection events: {jObject["error_message"].ToString()}");
                            }
                            catch (Exception e)
                            {
                                PrintError($"An exception was thrown whilst logging player connection events: {e.Message}");

                                if (debugLogResponseErrors)
                                    PrintError($"Associated response: {response}");

                                if (debugLogStackTraces)
                                    PrintError(e.StackTrace);
                            }

                            return;
                        }

                        break;
                }

                PrintError($"An unhandled error occurred whilst logging player connection events (response code: {code}).");
            }, this, RequestMethod.POST, AddToHeaders(headers, secretKey), 3000f);
        }

        private void ProcessCommands(List<Command> commands)
        {
            if (commands.Count < 1)
                return;

            List<Command> executedCommands = new List<Command>();

            foreach (Command command in commands)
            {
                string commandString = command.ReplaceVariables();

                if (debugLogActions)
                {
                    string type = command.Online ? "online" : "offline";
                    PrintWarning($"Processed {type} command for {command.PlayerId}: {commandString}");
                }
                
                executedCommands.Add(command);
                server.Command(commandString);

                if (executedCommands.Count == 15)
                {
                    DeleteCommands(executedCommands);
                    executedCommands.Clear();
                }
            }

            if (executedCommands.Count > 0)
                DeleteCommands(executedCommands);
        }

        private void ProcessOfflineCommands()
        {
            webrequest.Enqueue($"{BASE_URL}/queue/offline-commands", "", (code, response) =>
            {
                if (response != null)
                {
                    try
                    {
                        var jObject = JObject.Parse(response);

                        switch (code)
                        {
                            case 200:
                                break;
                            case 403:
                                StartValidationTimer(60f);
                                goto default;
                            default:
                                PrintWarning($"An error occurred whilst processing the offline commands: {jObject["error_message"].ToString()}");
                                return;
                        }

                        List<Command> commands = new List<Command>();

                        foreach (var command in (JArray) jObject["commands"])
                        {
                            int id = (int) command["id"];
                            string playerId = command["player"]["uuid"].ToString();
                            string playerName = command["player"]["name"].ToString();
                            string commandString = command["command"].ToString();

                            commands.Add(new Command(id, false, playerId, playerName, commandString));
                        }

                        ProcessCommands(commands);
                    }
                    catch (Exception e)
                    {
                        PrintError($"An exception was thrown whilst processing the offline commands: {e.Message}");

                        if (debugLogResponseErrors)
                            PrintError($"Associated response: {response}");

                        if (debugLogStackTraces)
                            PrintError(e.StackTrace);
                    }

                    return;
                }

                PrintError($"An unhandled error occurred whilst processing the offline commands (response code: {code}).");
            }, this, RequestMethod.GET, AddToHeaders(secretKey), 3000f);
        }

        private void ProcessOnlineCommands(IPlayer player, string shopPlayerId)
        {
            webrequest.Enqueue($"{BASE_URL}/queue/online-commands/{shopPlayerId}", "", (code, response) =>
            {
                if (response != null)
                {
                    try
                    {
                        var jObject = JObject.Parse(response);

                        switch (code)
                        {
                            case 200:
                                break;
                            case 403:
                                StartValidationTimer(60f);
                                goto default;
                            default:
                                PrintWarning($"An error occurred whilst processing the online commands for {player.Id}: {jObject["error_message"].ToString()}");
                                return;
                        }

                        List<Command> commands = new List<Command>();

                        foreach (var command in (JArray) jObject["commands"])
                        {
                            int id = (int) command["id"];
                            string commandString = command["command"].ToString();

                            commands.Add(new Command(id, true, player.Id, player.Name, commandString));
                        }

                        ProcessCommands(commands);
                    }
                    catch (Exception e)
                    {
                        PrintError($"An exception was thrown whilst processing the online commands for {player.Id}: {e.Message}");

                        if (debugLogResponseErrors)
                            PrintError($"Associated response: {response}");

                        if (debugLogStackTraces)
                            PrintError(e.StackTrace);
                    }

                    return;
                }

                PrintError($"An unhandled error occurred whilst processing the online commands for {player.Id} (response code: {code}).");
            }, this, RequestMethod.GET, AddToHeaders(secretKey), 3000f);
        }

        #endregion

        #region Web Request Timers

        private void StartEventTimer(float delay = 0f)
        {
            if (!validated || (eventTimer != null && !eventTimer.Destroyed))
                return;

            if (delay > 0f)
                eventTimer = timer.In(delay, () => LogEvents());
            else
                LogEvents();
        }

        private void StartValidationTimer(float delay = 0f)
        {
            if (validationTimer != null && !validationTimer.Destroyed)
                return;

            if (checkTimer != null && !checkTimer.Destroyed)
                checkTimer.Destroy();

            if (eventTimer != null && !eventTimer.Destroyed)
                eventTimer.Destroy();

            validated = false;

            if (delay > 0f)
                validationTimer = timer.In(delay, () => FetchStoreInformation(false, secretKey));
            else
                FetchStoreInformation(false, secretKey);
        }

        #endregion

        #region Utilities

        private Dictionary<string, string> AddToHeaders(string secretKey) => AddToHeaders(null, secretKey);

        private Dictionary<string, string> AddToHeaders(Dictionary<string, string> currentHeaders, string secretKey)
        {
            if (currentHeaders == null)
                currentHeaders = new Dictionary<string, string>();

            currentHeaders["X-Buycraft-Secret"] = secretKey;
            return currentHeaders;
        }

        private string FormattedUtcDate() => DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        private T GetConfig<T>(string name, T defaultValue) => Config[name] == null ? defaultValue : (T)Convert.ChangeType(Config[name], typeof(T));

        private T GetConfig<T>(string name, string name1, T defaultValue) => Config[name, name1] == null ? defaultValue : (T)Convert.ChangeType(Config[name, name1], typeof(T));

        private T GetConfig<T>(string name, string name1, string name2, T defaultValue) => Config[name, name1, name2] == null ? defaultValue : (T)Convert.ChangeType(Config[name, name1, name2], typeof(T));

        #endregion
    }
}
