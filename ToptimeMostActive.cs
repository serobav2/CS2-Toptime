using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using MySqlConnector;
using System;
using System.Data;
using System.Linq;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;
using System.Text.Json.Serialization;

namespace ToptimeMostActive
{
    public class ToptimeMostActiveConfig : BasePluginConfig
    {
        public override int Version { get; set; } = 2;

        [JsonPropertyName("DatabaseHost")]
        public string DatabaseHost { get; set; } = "";

        [JsonPropertyName("DatabasePort")]
        public int DatabasePort { get; set; } = 3306;

        [JsonPropertyName("DatabaseUser")]
        public string DatabaseUser { get; set; } = "";

        [JsonPropertyName("DatabasePassword")]
        public string DatabasePassword { get; set; } = "";

        [JsonPropertyName("DatabaseName")]
        public string DatabaseName { get; set; } = "";

        [JsonPropertyName("ChatMaxToptime")]
        public int ChatMaxToptime { get; set; } = 10;

        [JsonPropertyName("ConsoleMaxToptime")]
        public int ConsoleMaxToptime { get; set; } = 50;

        [JsonPropertyName("DiscordMaxToptime")]
        public int DiscordMaxToptime { get; set; } = 20;

        [JsonPropertyName("FileMaxToptime")]
        public int FileMaxToptime { get; set; } = 999;

        [JsonPropertyName("DiscordWebhook")]
        public string DiscordWebhook { get; set; } = "";
    }

    public class ToptimeMostActive : BasePlugin, IPluginConfig<ToptimeMostActiveConfig>
    {
        private MySqlConnection _dbConnection;

        public override string ModuleName => "Toptime";

        public override string ModuleVersion => "0.0.1";
        public ToptimeMostActiveConfig Config { get; set; } = null!;
        public void OnConfigParsed(ToptimeMostActiveConfig config) { Config = config; }

        public override void Load(bool hotReload)
        {
            Console.WriteLine("Toptime eklentisi başarıyla yüklendi.");
            RegisterListener<Listeners.OnMapEnd>(() => Unload(true));
            InitializeDatabase();
            AddTimer(60.0f, AddPlayerTime, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        }

        public override void Unload(bool hotReload)
        {
            _dbConnection?.Close();
        }

        private void InitializeDatabase()
        {
            string connectionString = $"Server={Config.DatabaseHost};Port={Config.DatabasePort};Database={Config.DatabaseName};User Id={Config.DatabaseUser};Password={Config.DatabasePassword};";

            try
            {
                _dbConnection = new MySqlConnection(connectionString);
                CreatePlayerTimesTable();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Toptime Hata: Veritabanına bağlanırken bir hata oluştu. {ex.Message}");
                Unload(true);
            }
        }

        private void CreatePlayerTimesTable()
        {
            try
            {
                string query = @"
                CREATE TABLE IF NOT EXISTS toptime (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    steam_id VARCHAR(32) NOT NULL UNIQUE,
                    player_name VARCHAR(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL,
                    playtime INT NOT NULL DEFAULT 1
                )";


                if (_dbConnection.State == ConnectionState.Closed) _dbConnection.Open();
                using (MySqlCommand command = new MySqlCommand(query, _dbConnection))
                {
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Toptime Hata: Tablo oluşturulurken bir hata oluştu. {ex.Message}");
            }
        }

        public void AddPlayerTime()
        {
            try
            {
                Utilities.GetPlayers().ForEach(player =>
                {
                    if (player is CCSPlayerController player2 && player2 != null && player2.IsValid && !player2.IsBot) AddTimeToDatabase(player2);
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine("Toptime Hata: " + ex.Message.ToString());
            }

        }

        private void AddTimeToDatabase(CCSPlayerController player)
        {
            try
            {
                string steamID = new SteamID(player.SteamID).SteamId2.ToString();
                if (!string.IsNullOrEmpty(steamID))
                {
                    string query = $"INSERT INTO toptime (steam_id, player_name) VALUES ('{steamID}', '{player.PlayerName}') ON DUPLICATE KEY UPDATE player_name = '{player.PlayerName}', playtime = playtime + 1";
                    if (_dbConnection.State == ConnectionState.Closed) _dbConnection.Open();
                    using (MySqlCommand command = new MySqlCommand(query, _dbConnection))
                    {
                        command.ExecuteNonQuery();
                    }
                    player.PrintToConsole("Toptime: Sürenize 1 dakika eklendi.");
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Toptime Hata: Oyuncu süresi eklenirken bir hata oluştu. {ex.Message}");
            }
        }

        [ConsoleCommand("toptime")]
        public void OnToptime(CCSPlayerController? player, CommandInfo command)
        {
            try
            {
                string query = $"SELECT player_name, steam_id, playtime FROM toptime ORDER BY playtime DESC LIMIT {Math.Max(Config.ChatMaxToptime, Config.ConsoleMaxToptime)}";

                if (_dbConnection.State == ConnectionState.Closed)
                    _dbConnection.Open();

                using (MySqlCommand sqlCommand = new MySqlCommand(query, _dbConnection))
                {
                    using (MySqlDataReader reader = sqlCommand.ExecuteReader())
                    {
                        if (!reader.HasRows)
                        {
                            if (player != null)
                            {
                                if (Config.ConsoleMaxToptime > 0) player.PrintToConsole("Toptime: Listede kayıtlı oyuncu yok.");
                                if (Config.ChatMaxToptime > 0) player.PrintToChat($"Toptime: {ChatColors.LightRed}Listede kayıtlı oyuncu yok.");
                            }
                            else Console.WriteLine("Toptime: Listede kayıtlı oyuncu yok.");
                            return;
                        }
                        if (player != null)
                        {
                            if (Config.ConsoleMaxToptime > 0) player.PrintToConsole($"Toptime: En yüksek süreye sahip {Config.ChatMaxToptime} oyuncu:");
                            if (Config.ChatMaxToptime > 0) player.PrintToChat($"Toptime: {ChatColors.Green}En yüksek süreye sahip {ChatColors.Red}{Config.ChatMaxToptime} {ChatColors.Green}oyuncu:");
                        }
                        else Console.WriteLine($"Toptime: En yüksek süreye sahip {Config.ChatMaxToptime} oyuncu:");

                        int countConsole = 0;
                        int countChat = 0;
                        while (reader.Read())
                        {
                            if (countConsole < Config.ConsoleMaxToptime)
                            {
                                countConsole++;
                                if (player != null) player.PrintToConsole($"{countConsole}. {reader["player_name"].ToString()} [{reader["steam_id"].ToString()}]: {Convert.ToInt32(reader["playtime"])} dakika");
                                else Console.WriteLine($"{countConsole}. {reader["player_name"].ToString()} [{reader["steam_id"].ToString()}]: {Convert.ToInt32(reader["playtime"])} dakika");
                            }
                            if (player != null && countChat < Config.ChatMaxToptime)
                            {
                                countChat++;
                                player.PrintToChat($"{countChat}. {ChatColors.LightPurple}{reader["player_name"].ToString()} {ChatColors.BlueGrey}[{reader["steam_id"].ToString()}]{ChatColors.Gold}: {ChatColors.Magenta}{Convert.ToInt32(reader["playtime"])} dakika");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Toptime Hata: Top süreleri alınırken bir hata oluştu. {ex.Message}");
                if (player != null)
                {
                    player.PrintToConsole($"Toptime Hata: Top süreleri alınırken bir hata oluştu. {ex.Message}");
                    player.PrintToChat($"Toptime Hata: {ChatColors.LightRed}Top süreleri alınırken bir hata oluştu. {ex.Message}");
                }
            }
        }

        [ConsoleCommand("surem")]
        public void OnMyToptime(CCSPlayerController? player, CommandInfo command)
        {
            try
            {
                if (player != null)
                {
                    string query = $"SELECT playtime FROM toptime WHERE steam_id = '{new SteamID(player.SteamID).SteamId2.ToString()}'";

                    if (_dbConnection.State == ConnectionState.Closed)
                        _dbConnection.Open();

                    using (MySqlCommand sqlCommand = new MySqlCommand(query, _dbConnection))
                    {
                        object result = sqlCommand.ExecuteScalar();
                        int playtime = 0;
                        if (result != null && result != DBNull.Value) playtime = Convert.ToInt32(result);
                        player.PrintToConsole($"Toptime: Toplam oynama süreniz {playtime} dakika.");
                        player.PrintToChat($"Toptime: {ChatColors.Green}Toplam oynama süreniz {ChatColors.Magenta}{playtime} dakika.");
                    }
                }
                else Console.WriteLine($"Toptime Hata: Bu komutu sadece oyuncular kullanabilir.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Toptime Hata: Oyuncu süre bilgisi alınırken bir hata oluştu. {ex.Message}");
                if (player != null)
                {
                    player.PrintToConsole($"Toptime Hata: Oyuncu süre bilgisi alınırken bir hata oluştu. {ex.Message}");
                    player.PrintToChat($"Toptime Hata: {ChatColors.LightRed}Oyuncu süre bilgisi alınırken bir hata oluştu. {ex.Message}");
                }
            }
        }

        [RequiresPermissions("@css/root")]
        [ConsoleCommand("toptimeyazdir")]
        public void OnToptimePrint(CCSPlayerController? player, CommandInfo command)
        {
            try
            {
                string query = $"SELECT player_name, steam_id, playtime FROM toptime ORDER BY playtime DESC LIMIT {Config.FileMaxToptime}";

                if (_dbConnection.State == ConnectionState.Closed)
                    _dbConnection.Open();

                DateTime now = DateTime.Now;
                string directoryPath = Path.Combine(ModuleDirectory, "logs");
                string fileName = $"{now:yyyyMMdd_HHmmss}.txt";
                string filePath = Path.Combine(directoryPath, fileName);

                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }
                int count = 0;
                using (MySqlCommand sqlCommand = new MySqlCommand(query, _dbConnection))
                {
                    using (MySqlDataReader reader = sqlCommand.ExecuteReader())
                    {


                        using (StreamWriter writer = new StreamWriter(filePath))
                        {
                            while (reader.Read())
                            {
                                count++;
                                string line = $"{count}. {reader["player_name"].ToString()} [{reader["steam_id"].ToString()}]: {Convert.ToInt32(reader["playtime"])} dakika";
                                writer.WriteLine(line);
                            }
                        }
                    }
                }


                if (player != null)
                {
                    player.PrintToConsole($"Toptime: {fileName} dosyasına {count} kayıt yazıldı.");
                    player.PrintToChat($"Toptime: {ChatColors.Green}{fileName} {ChatColors.BlueGrey}dosyasına {ChatColors.Green}{count} {ChatColors.BlueGrey}kayıt yazıldı.");
                }
                else Console.WriteLine($"Toptime: {fileName} dosyasına {count} kayıt yazıldı.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Toptime Hata: Top süreleri alınırken bir hata oluştu. {ex.Message}");

                if (player != null)
                {
                    player.PrintToConsole($"Toptime Hata: Top süreleri alınırken bir hata oluştu. {ex.Message}");
                    player.PrintToChat($"Toptime Hata: {ChatColors.LightRed}Top süreleri alınırken bir hata oluştu. {ex.Message}");
                }
            }
        }

        [RequiresPermissions("@css/root")]
        [ConsoleCommand("toptimegonder")]
        public async void OnToptimSend(CCSPlayerController? player, CommandInfo command)
        {
            try
            {
                if (!string.IsNullOrEmpty(Config.DiscordWebhook))
                {
                    string query = $"SELECT player_name, steam_id, playtime FROM toptime ORDER BY playtime DESC LIMIT {Config.DiscordMaxToptime}";

                    if (_dbConnection.State == ConnectionState.Closed)
                        _dbConnection.Open();
                    int count = 0;
                    string line = "";
                    using (MySqlCommand sqlCommand = new MySqlCommand(query, _dbConnection))
                    {
                        using (MySqlDataReader reader = sqlCommand.ExecuteReader())
                        {

                            while (reader.Read())
                            {
                                count++;
                                line += $"{count}. {reader["player_name"].ToString()} [{reader["steam_id"].ToString()}]: {Convert.ToInt32(reader["playtime"])} dakika\n";
                            }

                        }
                    }
                    var body = JsonSerializer.Serialize(new { content = "# TOPTİME\n" + line });
                    var content = new StringContent(body, Encoding.UTF8, "application/json");
                    HttpClient _httpClient = new HttpClient();
                    _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    HttpResponseMessage res = (await _httpClient.PostAsync(Config.DiscordWebhook, content)).EnsureSuccessStatusCode();


                    if (player != null)
                    {
                        player.PrintToConsole($"Toptime: Discorda {count} kayıt gönderildi.");
                        player.PrintToChat($"Toptime: {ChatColors.Magenta}Discorda {ChatColors.Green}{count} {ChatColors.BlueGrey}kayıt gönderildi.");
                    }
                    else Console.WriteLine($"Toptime: Discorda dosyasına {count} kayıt gönderildi.");
                }


            }
            catch (Exception ex)
            {
                Console.WriteLine($"Toptime Hata: Top süreleri alınırken bir hata oluştu. {ex.Message}");

                if (player != null)
                {
                    player.PrintToConsole($"Toptime Hata: Top süreleri alınırken bir hata oluştu. {ex.Message}");
                    player.PrintToChat($"Toptime Hata: {ChatColors.LightRed}Top süreleri alınırken bir hata oluştu. {ex.Message}");
                }
            }
        }

        [RequiresPermissions("@css/root")]
        [ConsoleCommand("toptime0")]
        public void OnToptimeClear(CCSPlayerController? player, CommandInfo command)
        {
            try
            {
                string clearQuery = "DELETE FROM toptime";

                if (_dbConnection.State == ConnectionState.Closed)
                    _dbConnection.Open();

                using (MySqlCommand clearCommand = new MySqlCommand(clearQuery, _dbConnection))
                {
                    clearCommand.ExecuteNonQuery();
                }

                if (player != null)
                {
                    player.PrintToConsole("Toptime: Tüm oyuncu süre verileri silindi.");
                    player.PrintToChat($"Toptime: {ChatColors.Green}Tüm oyuncu süre verileri silindi.");
                }
                else Console.WriteLine("Toptime: Tüm oyuncu süre verileri silindi.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Toptime Hata: Tüm oyuncu süre verileri silinirken bir hata oluştu. {ex.Message}");

                if (player != null)
                {
                    player.PrintToConsole($"Toptime Hata: Tüm oyuncu süre verileri silinirken bir hata oluştu. {ex.Message}");
                    player.PrintToChat($"Toptime Hata: {ChatColors.LightRed}Tüm oyuncu süre verileri silinirken bir hata oluştu. {ex.Message}");
                }
            }
        }
    }
}
