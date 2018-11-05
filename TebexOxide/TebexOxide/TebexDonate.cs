using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using TebexDonate.Commands;
using TebexDonate.Models;
using TebexDonate.PushCommands;
using Timer = System.Threading.Timer;


namespace Oxide.Plugins
{
    [Info("Tebex Donate", "Tebex", "1.0.0", ResourceId = 0)]
    [Description("uMod Plugin for the Tebex Server Monitization Platform.")]
    public class TebexDonate : CovalencePlugin
    {

        public int nextCheck = 15 * 60;
        public WebstoreInfo information;
        private DateTime _lastCalled = DateTime.Now.AddMinutes(-14);

        public static TebexDonate Instance;

        public static Timer timerRef;
        
        private String secret = "";
        private String baseUrl = "https://plugin.buycraft.net/";
        private bool buyEnabled = true;
        protected override void LoadDefaultConfig()
        {
            Puts("Creating a new configuration file!");
            if (Config["secret"] == null) Config["secret"] = secret;
            if (Config["baseUrl"] == null) Config["baseUrl"] = baseUrl;
            if (Config["buyEnabled"] == null) Config["buyEnabled"] = true;
            if (Config["pushCommands"] == null) Config["pushCommands"] = true;
            if (Config["pushCommandsPort"] == null) Config["pushCommandPort"] = "3000";
            
            SaveConfig();            
        }     
        
        void OnServerInitialized()
        {
            this.information = new WebstoreInfo();
            Instance = this;
            if ((string) Instance.Config["secret"] == "")
            {
                Puts("You have not yet defined your secret key. Use 'tebex:secret <secret>' to define your key");
            }
            else
            {
                cmdInfo(null, "tebex:info", null);
            }

            timerRef = Instance.timer.Every(60, () =>
            {
                Instance.checkQueue();
            });
            
        }

        private void Loaded()
        {
        lang.RegisterMessages(new Dictionary<string, string>
            {
                ["WebstoreUrl"] = "To buy packages from our webstore, please visit {webstoreUrl}.",
            }, this);

            if ((bool)Config["pushCommands"])
            {
                var server = new HttpAsyncServer(new string[] {"http://localhost:" + (string) Config["pushCommandsPort"] + "/"});
                server.RunServer();
            }

        }

        private void Unload()
        {
            timerRef.Destroy();
        }
        
        private void checkQueue()
        {
            if ((DateTime.Now - this._lastCalled).TotalSeconds > Instance.nextCheck)
            {
                this._lastCalled = DateTime.Now;
                //Do Command Check             
                cmdForcecheck(null, "tebex:forcecheck", null);
            }
        }

        public void UpdateConfig()
        {
            this.SaveConfig();
        }
        
        public IPlayer getPlayerById(String id)
        {
            return players.FindPlayerById(id);
        }

        public bool isPlayerOnline(IPlayer player)
        {            
            return player.IsConnected;
        }

        public void runCommand(string cmd)
        {
            server.Command(cmd);
        }

        [Command("tebex:info")]
        void cmdInfo(IPlayer player, String cmd, String[] args)
        {
            if (player == null || player.IsAdmin)
            {
                CommandTebexInfo cmdCommandTebexInfo = new CommandTebexInfo();
                cmdCommandTebexInfo.Execute(player, cmd, args);
            }
        }
        
        [Command("tebex:secret")]
        void cmdSecret(IPlayer player, String cmd, String[] args)
        {
            if (player.IsServer)
            {
                CommandTebexSecret cmdCommandTebexSecret = new CommandTebexSecret();
                cmdCommandTebexSecret.Execute(player, cmd, args);
            }
        }
        
        [ConsoleCommand("tebex:secret")]
        void cmdSecretConsole(IPlayer player, String cmd, String[] args)
        {
            if (player.IsServer)
            {
                CommandTebexSecret cmdCommandTebexSecret = new CommandTebexSecret();
                cmdCommandTebexSecret.Execute(player, cmd, args);
            }
        }        

        [Command("tebex:forcecheck")]
        void cmdForcecheck(IPlayer player, String cmd, String[] args)
        {
            if (player == null || player.IsAdmin)
            {
                CommandTebexForcecheck cmdCommandTebexForcecheck = new CommandTebexForcecheck();
                cmdCommandTebexForcecheck.Execute(player, cmd, args);
            }
        }

        [Command("buy")]
        void cmdBuy(IPlayer player, String cmd, String[] args)
        {
            player.Message(lang.GetMessage("WebstoreUrl", this, player.Id).Replace("{webstoreUrl}", Instance.information.domain));
        }
        
    }
}

namespace TebexDonate.Commands
{
    
    public interface ITebexCommand
    {        
        void Execute(IPlayer player, String cmd, String[] args);
        
        void HandleResponse(JObject response);

        void HandleError(Exception e);
        
    }  
    
    public class CommandTebexSecret : ITebexCommand
    {

        public void Execute(IPlayer player, String cmd, String[] args)
        {

            String secret = args[0];

            Oxide.Plugins.TebexDonate.Instance.Config["secret"] = secret;
            Oxide.Plugins.TebexDonate.Instance.UpdateConfig();
            // Set a custom timeout (in milliseconds)
            var timeout = 2000f;

            // Set some custom request headers (eg. for HTTP Basic Auth)
            var headers = new Dictionary<string, string> { { "X-Buycraft-Secret", (string) Oxide.Plugins.TebexDonate.Instance.Config["secret"] } };
            WebRequests webrequest = new WebRequests();
            webrequest.EnqueueGet((string) Oxide.Plugins.TebexDonate.Instance.Config["baseUrl"] + "information", (code, response) =>
            {
                if (response == null || code != 200)
                {
                    HandleError(new Exception("Error: code" + code.ToString()));
                    webrequest.Shutdown();
                    return;
                }
                
                HandleResponse(JObject.Parse(response));
                webrequest.Shutdown();
                
            }, Oxide.Plugins.TebexDonate.Instance, headers, timeout);

        }

        public void HandleResponse(JObject response)
        {
            Oxide.Plugins.TebexDonate.Instance.information.id = (int) response["account"]["id"];
            Oxide.Plugins.TebexDonate.Instance.information.domain = (string) response["account"]["domain"];
            Oxide.Plugins.TebexDonate.Instance.information.gameType = (string) response["account"]["game_type"];
            Oxide.Plugins.TebexDonate.Instance.information.name = (string) response["account"]["name"];
            Oxide.Plugins.TebexDonate.Instance.information.currency = (string) response["account"]["currency"]["iso_4217"];
            Oxide.Plugins.TebexDonate.Instance.information.currencySymbol = (string) response["account"]["currency"]["symbol"];
            Oxide.Plugins.TebexDonate.Instance.information.serverId = (int) response["server"]["id"];
            Oxide.Plugins.TebexDonate.Instance.information.serverName = (string) response["server"]["name"];
            
            Interface.Oxide.LogInfo("Your secret key has been validated! Webstore Name: " + Oxide.Plugins.TebexDonate.Instance.information.name);
        }

        public void HandleError(Exception e)
        {
            Interface.Oxide.LogError("We were unable to validate your secret key.");
        }      
    }
    
    public class CommandTebexInfo : ITebexCommand
    {      
        public void Execute(IPlayer player, String cmd, String[] args)
        {           
            // Set a custom timeout (in milliseconds)
            var timeout = 2000f;

            // Set some custom request headers (eg. for HTTP Basic Auth)
            var headers = new Dictionary<string, string> { { "X-Buycraft-Secret", (string) Oxide.Plugins.TebexDonate.Instance.Config["secret"] } };

            WebRequests webrequest = new WebRequests();
            webrequest.EnqueueGet((string) Oxide.Plugins.TebexDonate.Instance.Config["baseUrl"] + "information", (code, response) =>
            {
                if (response == null || code != 200)
                {
                    HandleError(new Exception("Error"));
                    webrequest.Shutdown();
                    return;
                }
                
                HandleResponse(JObject.Parse(response));
                webrequest.Shutdown();
                
            }, Oxide.Plugins.TebexDonate.Instance, headers, timeout);

        }

        public void HandleResponse(JObject response)
        {
            Oxide.Plugins.TebexDonate.Instance.information.id = (int) response["account"]["id"];
            Oxide.Plugins.TebexDonate.Instance.information.domain = (string) response["account"]["domain"];
            Oxide.Plugins.TebexDonate.Instance.information.gameType = (string) response["account"]["game_type"];
            Oxide.Plugins.TebexDonate.Instance.information.name = (string) response["account"]["name"];
            Oxide.Plugins.TebexDonate.Instance.information.currency = (string) response["account"]["currency"]["iso_4217"];
            Oxide.Plugins.TebexDonate.Instance.information.currencySymbol = (string) response["account"]["currency"]["symbol"];
            Oxide.Plugins.TebexDonate.Instance.information.serverId = (int) response["server"]["id"];
            Oxide.Plugins.TebexDonate.Instance.information.serverName = (string) response["server"]["name"];
            
            Interface.Oxide.LogInfo("Server Information");
            Interface.Oxide.LogInfo("=================");
            Interface.Oxide.LogInfo("Server "+Oxide.Plugins.TebexDonate.Instance.information.serverName+" for webstore "+Oxide.Plugins.TebexDonate.Instance.information.name+"");
            Interface.Oxide.LogInfo("Server prices are in "+Oxide.Plugins.TebexDonate.Instance.information.currency+"");
            Interface.Oxide.LogInfo("Webstore domain: "+Oxide.Plugins.TebexDonate.Instance.information.domain+"");
        }

        public void HandleError(Exception e)
        {
            Interface.Oxide.LogError("We are unable to fetch your server details. Please check your secret key.");
        }      
    }    
    
    public class CommandTebexForcecheck: ITebexCommand
    {

        public void Execute(IPlayer player, String cmd, String[] args)
        {           
            Interface.Oxide.LogInfo("Checking for commands to be executed...");
            // Set a custom timeout (in milliseconds)
            var timeout = 2000f;

            // Set some custom request headers (eg. for HTTP Basic Auth)
            var headers = new Dictionary<string, string> { { "X-Buycraft-Secret", (string) Oxide.Plugins.TebexDonate.Instance.Config["secret"] } };

            WebRequests webrequest = new WebRequests();
            webrequest.EnqueueGet((string) Oxide.Plugins.TebexDonate.Instance.Config["baseUrl"] + "queue", (code, response) =>
            {
                if (response == null || code != 200)
                {
                    HandleError(new Exception("Error"));
                    webrequest.Shutdown();
                    return;
                }
                
                HandleResponse(JObject.Parse(response));
                webrequest.Shutdown();
                
            }, Oxide.Plugins.TebexDonate.Instance, headers, timeout);

        }

        public void HandleResponse(JObject response)
        {
            if ((int) response["meta"]["next_check"] > 0)
            {
                Oxide.Plugins.TebexDonate.Instance.nextCheck = (int) response["meta"]["next_check"];
            }
            
            if ((bool) response["meta"]["execute_offline"])
            {
                try
                {
                    TebexCommandRunner.doOfflineCommands();
                }
                catch (Exception e)
                {
                    Interface.Oxide.LogError(e.ToString());
                }
            }
            
            JArray players = (JArray) response["players"];

            foreach (var player in players)
            {
                try
                {
                    IPlayer targetPlayer = Oxide.Plugins.TebexDonate.Instance.getPlayerById((string) player["uuid"]);                    

                    if (targetPlayer != null && Oxide.Plugins.TebexDonate.Instance.isPlayerOnline(targetPlayer))
                    {
                        Interface.Oxide.LogInfo("Execute commands for " + targetPlayer.Name + "(ID: "+targetPlayer.Id+")");
                        TebexCommandRunner.doOnlineCommands((int) player["id"], (string) targetPlayer.Name, targetPlayer.Id);
                    }
                }
                catch (Exception e)
                {
                    Interface.Oxide.LogError(e.Message);
                }
            }
        }

        public void HandleError(Exception e)
        {
            Interface.Oxide.LogError("We are unable to fetch your server queue. Please check your secret key.");
            Interface.Oxide.LogError(e.ToString());
        }      
    }    
    
    public class TebexCommandRunner
    {

        public static int deleteAfter = 3;
        
        public static void doOfflineCommands()
        {
            String url = Oxide.Plugins.TebexDonate.Instance.Config["baseUrl"] + "queue/offline-commands";

            // Set a custom timeout (in milliseconds)
            var timeout = 2000f;

            // Set some custom request headers (eg. for HTTP Basic Auth)
            var headers = new Dictionary<string, string> { { "X-Buycraft-Secret", (string) Oxide.Plugins.TebexDonate.Instance.Config["secret"] } };

            WebRequests webrequest = new WebRequests();
            webrequest.EnqueueGet(url, (code, response) =>
            {
                JObject json = JObject.Parse(response);
                JArray commands = (JArray) json["commands"];

                int exCount = 0;
                List<int> executedCommands = new List<int>();
                
                foreach (var command in commands.Children())
                {
                    String commandToRun = buildCommand((string) command["command"], (string) command["player"]["name"],
                        (string) command["player"]["uuid"]);
                    
                    Interface.Oxide.LogInfo("Run command " + commandToRun);
                    Oxide.Plugins.TebexDonate.Instance.runCommand(commandToRun);
                    executedCommands.Add((int) command["id"]);

                    exCount++;

                    if (exCount % deleteAfter == 0)
                    {
                        try
                        {
                            deleteCommands(executedCommands);
                            executedCommands.Clear();
                        }
                        catch (Exception ex)
                        {
                            Interface.Oxide.LogError(ex.ToString());
                        }
                    }
                    
                }
                
                Interface.Oxide.LogInfo(exCount.ToString() + " offline commands executed");
                if (exCount % deleteAfter != 0)
                {
                    try
                    {
                        deleteCommands(executedCommands);
                        executedCommands.Clear();
                    }
                    catch (Exception ex)
                    {
                        Interface.Oxide.LogError(ex.ToString());
                    }
                }

                webrequest.Shutdown();
            }, Oxide.Plugins.TebexDonate.Instance, headers, timeout);
        }

        public static void doOnlineCommands(int playerPluginId, string playerName, string playerId)
        {
            
            Interface.Oxide.LogInfo("Running online commands for "+playerName+" (" + playerId + ")");
            

            String url = Oxide.Plugins.TebexDonate.Instance.Config["baseUrl"] + "queue/online-commands/" + playerPluginId.ToString();

            // Set a custom timeout (in milliseconds)
            var timeout = 2000f;

            // Set some custom request headers (eg. for HTTP Basic Auth)
            var headers = new Dictionary<string, string> { { "X-Buycraft-Secret", (string) Oxide.Plugins.TebexDonate.Instance.Config["secret"] } };

            WebRequests webrequest = new WebRequests();
            webrequest.EnqueueGet(url, (code, response) =>
            {
                JObject json = JObject.Parse(response);
                JArray commands = (JArray) json["commands"];

                int exCount = 0;
                List<int> executedCommands = new List<int>();
                
                foreach (var command in commands.Children())
                {

                    String commandToRun = buildCommand((string) command["command"], playerName, playerId);
                    
                    Interface.Oxide.LogInfo("Run command " + commandToRun);
                    Oxide.Plugins.TebexDonate.Instance.runCommand(commandToRun);
                    executedCommands.Add((int) command["id"]);

                    exCount++;

                    if (exCount % deleteAfter == 0)
                    {
                        try
                        {
                            deleteCommands(executedCommands);
                            executedCommands.Clear();
                        }
                        catch (Exception ex)
                        {
                            Interface.Oxide.LogError(ex.ToString());
                        }
                    }
                    
                }
                
                Interface.Oxide.LogInfo(exCount.ToString() + " online commands executed for " + playerName);
                if (exCount % deleteAfter != 0)
                {
                    try
                    {
                        deleteCommands(executedCommands);
                        executedCommands.Clear();
                    }
                    catch (Exception ex)
                    {
                        Interface.Oxide.LogError(ex.ToString());
                    }
                }

                webrequest.Shutdown();
            }, Oxide.Plugins.TebexDonate.Instance, headers, timeout);

        }

        public static void deleteCommands(List<int> commandIds)
        {

            String url = Oxide.Plugins.TebexDonate.Instance.Config["baseUrl"] + "queue?";
            String amp = "";

            foreach (int CommandId in commandIds)
            {
                url = url + amp + "ids[]=" + CommandId;
                amp = "&";
            }

            // Set a custom timeout (in milliseconds)
            var timeout = 2000f;

            // Set some custom request headers (eg. for HTTP Basic Auth)
            var headers = new Dictionary<string, string> { { "X-Buycraft-Secret", (string) Oxide.Plugins.TebexDonate.Instance.Config["secret"] } };
            
            WebRequests webrequest = new WebRequests();
            webrequest.Enqueue(url, "", (code, response) =>
            {
                webrequest.Shutdown();
                
            }, Oxide.Plugins.TebexDonate.Instance, RequestMethod.DELETE, headers, timeout);                        
        }

        public static string buildCommand(string command, string username, string id)
        {
            return command.Replace("{id}", id).Replace("{username}", username);
        }
    }    
}

namespace TebexDonate.PushCommands
{
    class HttpAsyncServer
    {
        private string[] listenedAddresses;
        private bool isWorked;
        private HttpListener listener;

        public HttpAsyncServer(string[] listenedAddresses)
        {
            Interface.Oxide.LogError("Starting push server on " + listenedAddresses + "...");
            this.listenedAddresses = listenedAddresses;
            isWorked = false;
        }

        private void HandleRequest(HttpListenerContext context)
        {
        }

        private void work()
        {
            listener = new HttpListener();
            foreach (var prefix in listenedAddresses)
                listener.Prefixes.Add(prefix);

            listener.Start();

            while (isWorked)
            {
                try
                {
                    var context = listener.GetContext();
                    string responseString = "<HTML><BODY> Hello world!</BODY></HTML>";
                    byte[] buffer = Encoding.UTF8.GetBytes(responseString);

                    context.Response.ContentLength64 = buffer.Length;
                    System.IO.Stream output = context.Response.OutputStream;
                    output.Write(buffer, 0, buffer.Length);
                    // You must close the output stream.
                    output.Close();
                }
                catch (Exception)
                {

                }
            }

            stop();
        }

        public void stop()
        {
            isWorked = false;
            listener.Stop();
        }


        public void RunServer()
        {
            if (isWorked)
                Interface.Oxide.LogError("Server already started");

            isWorked = true;

            Timer t = new Timer((thread) => { work(); });
            t.Change(1, Timeout.Infinite);
            Thread.Sleep(10);
        }
    }
}

namespace TebexDonate.Models
{
    public class WebstoreInfo
    {
        public int id;
        public string name;
        public string domain;
        public string currency;
        public string currencySymbol;
        public string gameType;
        public string serverName;
        public int serverId;
    }
}