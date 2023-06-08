using System;
using System.Net.Http;
using Bitcoin.BitcoinUtilities;
using Bitcoin.BIP39;
using System.Threading.Tasks;
using NBitcoin;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Net;
using Knapcode.TorSharp;
using System.Runtime.InteropServices;
//using System.Windows;

namespace BitCheck
{
    public class Program
    {
        private static int Counter = 0;
        private static HttpClient _httpClient;
        private static TorSharpSettings settings = new TorSharpSettings
        {
            PrivoxySettings = { Port = 1337 },
            ZippedToolsDirectory = Path.Combine(Environment.CurrentDirectory, "TorZipped"),
            ExtractedToolsDirectory = Path.Combine(Environment.CurrentDirectory, "TorExtracted"),
            TorSettings =
                {
                    SocksPort = 1338,
                    ControlPort = 1339,
                    ControlPassword = "foobar",
                },
        };
        private static TorSharpProxy proxy;

        public static int bipIndex;
        public static int errorCount = 0;
        public static Dictionary<string, Balance> CurrentWalletInfo { get; set; }
        public static List<AliveWallet> aliveWallets = new List<AliveWallet>();
        public static List<AliveWallet> maybeAliveWallets = new List<AliveWallet>();
        public static List<Thread> AppThreads = new List<Thread>();
        public static string[] BIPS = new string[] { "m/0/0", "m/44'/0'/0'/0/0", "m/49'/0'/0'/0/0", "m/84'/0'/0'/0/0" };
        private static BIP39 bip;
        static async Task Main(string[] args)
        {
            using (var httpClientLoader = new HttpClient())
            {
                var fetcher = new TorSharpToolFetcher(settings, httpClientLoader);
                await fetcher.FetchAsync();
            }
            proxy = new TorSharpProxy(settings);
            await proxy.ConfigureAndStartAsync();

            HttpClientHandler handler = new HttpClientHandler()
            {
                Proxy = new WebProxy(new Uri("http://localhost:" + settings.PrivoxySettings.Port))
            };
            _httpClient = new HttpClient(handler);
            var result = _httpClient.GetStringAsync("https://check.torproject.org/api/ip").Result.ToString();
            Console.WriteLine();
            Console.WriteLine("Are we using Tor?");
            Console.WriteLine(result);
            Console.WriteLine();
            Console.WriteLine("Enter BIP: \n 0. BIP32 \n 1. BIP44 \n 2. BIP49 \n 3. BIP84");
            bipIndex = int.Parse(Console.ReadLine());
            Console.WriteLine("Enter thread's count:");
            int threadCount = int.Parse(Console.ReadLine());

            for (int i = 0; i < threadCount; i++)
            {
                AppThreads.Add(new Thread(Execute));
                AppThreads[i].Start(i);
            }

            Console.ReadKey();
        }

        private static object locker = new object();

        public async static void Execute(object? obj)
        {
            try
            {
                do
                {
                   
                    bip = await BIP39.GetBIP39Async(128, "", BIP39.Language.English);
                    //Console.WriteLine(bip.MnemonicSentence);

                    Mnemonic resoteMnemonic = new Mnemonic(bip.MnemonicSentence);
                    ExtKey masterKey = resoteMnemonic.DeriveExtKey();
                    KeyPath keyPath = new KeyPath(BIPS[bipIndex]);
                    ExtKey key = masterKey.Derive(keyPath);

                    string adress = key.PrivateKey.PubKey.GetAddress(ScriptPubKeyType.Segwit, Network.Main).ToString();

                    HttpResponseMessage response = await _httpClient.GetAsync("https://blockchain.info/balance?active=" + adress);



                    if (response.IsSuccessStatusCode)
                    {
                        using (StreamReader reader = new StreamReader(await response.Content.ReadAsStreamAsync(), Encoding.UTF8))
                        {
                            string jsonString = reader.ReadToEnd();

                            CurrentWalletInfo = JsonConvert.DeserializeObject<Dictionary<string, Balance>>(jsonString);

                            foreach (var item in CurrentWalletInfo)
                            {
                                if (item.Value.final_balance <= 0 && item.Value.n_tx > 0)
                                {
                                    Console.BackgroundColor = ConsoleColor.DarkYellow;
                                    Console.ForegroundColor = ConsoleColor.White;

                                    AliveWallet maybeAlive = new AliveWallet()
                                    {
                                        Adress = item.Key,
                                        Seed = bip.MnemonicSentence,
                                        Balance = item.Value.total_received
                                    };
                                    maybeAliveWallets.Add(maybeAlive);
                                    await SaveData(false);
                                }
                                if (item.Value.final_balance > 0)
                                {
                                    Console.ForegroundColor = ConsoleColor.White;
                                    Console.BackgroundColor = ConsoleColor.Green;

                                    AliveWallet alive = new AliveWallet()
                                    {
                                        Adress = item.Key,
                                        Seed = bip.MnemonicSentence,
                                        Balance = item.Value.final_balance,
                                    };
                                    aliveWallets.Add(alive);
                                    await SaveData(true);
                                }
                                Counter++;
                                Console.WriteLine($"#{Counter} Wallet - {item.Key}, have {item.Value.final_balance} BTC, was {item.Value.total_received} BTC, and th count - {item.Value.n_tx}");
                                Console.ForegroundColor = ConsoleColor.White;
                                Console.BackgroundColor = ConsoleColor.Black;
                            }
                        }
                    }
                    else
                    {
                        lock (locker)
                        {
                            errorCount++;
                            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                            {
                                if (errorCount == 1)
                                {
                                    proxy.GetNewIdentityAsync().Wait(2000);
                                }
                            }
                            errorCount = 0;
                        }
                       
                        Console.WriteLine(response.StatusCode.ToString());
                        Console.WriteLine(response.ReasonPhrase);
                        
                    }

                } while (true);
            }
            catch (Exception exc)
            {
                Console.WriteLine($"Last seed: {bip.MnemonicSentence}");
                Console.WriteLine(exc.ToString());
            }
        }



        async static Task SaveData(bool isAlive)
        {
            if (isAlive == true)
            {
                using (StreamWriter file = File.CreateText("alive.json"))
                {
                    JsonSerializer serializer = new JsonSerializer();
                    serializer.Serialize(file, aliveWallets);
                }
            }
            else
            {
                using (StreamWriter file = File.CreateText("aliveMaybe.json"))
                {
                    JsonSerializer serializer = new JsonSerializer();
                    serializer.Serialize(file, maybeAliveWallets);
                }

            }

        }
    }
}
