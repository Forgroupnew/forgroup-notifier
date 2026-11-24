
using System.Net.Http;
using System.Text.Json;
using System.Xml.Linq;

const string youtubeChannelId = "UCRdcY_BL4X8pLJu849h6kZg";
const string telegramChannel = "@forgroupofficial";

string? token = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");

if (string.IsNullOrWhiteSpace(token))
{
    Console.WriteLine("ОШИБКА: не найден токен Telegram.");
    Environment.ExitCode = 1;
    return;
}

string rssUrl =
    $"https://www.youtube.com/feeds/videos.xml?channel_id={youtubeChannelId}";

string statePath = Path.Combine(
    Directory.GetCurrentDirectory(), "last-video.txt");

using var http = new HttpClient
{
    Timeout = TimeSpan.FromSeconds(30)
};

Console.WriteLine("=================================");
Console.WriteLine("       FORGROUP NOTIFIER");
Console.WriteLine("=================================");
Console.WriteLine("Проверка YouTube...");

var videos = await GetVideosAsync(http, rssUrl);

if (videos.Count == 0)
{
    Console.WriteLine("Видео пока не найдены.");
    return;
}

string? lastVideoId = File.Exists(statePath)
    ? (await File.ReadAllTextAsync(statePath)).Trim()
    : null;

// Первый запуск: запоминаем последнее видео,
// но не публикуем старые видео.
if (string.IsNullOrWhiteSpace(lastVideoId))
{
    await File.WriteAllTextAsync(statePath, videos[0].VideoId);
    Console.WriteLine("Первый запуск: последнее видео сохранено.");
    return;
}

int lastIndex = videos.FindIndex(v => v.VideoId == lastVideoId);

if (lastIndex < 0)
{
    Console.WriteLine(
        "Предыдущее видео не найдено в RSS. " +
        "Сохраняем последнее видео без публикации.");

    await File.WriteAllTextAsync(statePath, videos[0].VideoId);
    return;
}

var newVideos = videos
    .Take(lastIndex)
    .Reverse()
    .ToList();

if (newVideos.Count == 0)
{
    Console.WriteLine("Новых видео нет.");
    return;
}

foreach (var video in newVideos)
{
    await SendNotificationAsync(
        http, token, telegramChannel, video);

    // Сохраняем ID только после успешной отправки.
    await File.WriteAllTextAsync(statePath, video.VideoId);

    Console.WriteLine($"Опубликовано: {video.Title}");
}

Console.WriteLine("Проверка завершена.");

static async Task<List<Video>> GetVideosAsync(
    HttpClient http, string rssUrl)
{
    string xml = await http.GetStringAsync(rssUrl);
    XDocument document = XDocument.Parse(xml);

    XNamespace atom = "http://www.w3.org/2005/Atom";
    XNamespace yt = "http://www.youtube.com/xml/schemas/2015";

    return document.Root?
        .Elements(atom + "entry")
        .Select(entry => new Video(
            entry.Element(yt + "videoId")?.Value ?? "",
            entry.Element(atom + "title")?.Value ?? "Новое видео"))
        .Where(v => !string.IsNullOrWhiteSpace(v.VideoId))
        .ToList() ?? new List<Video>();
}

static async Task SendNotificationAsync(
    HttpClient http,
    string token,
    string chatId,
    Video video)
{
    string videoUrl =
        $"https://www.youtube.com/watch?v={video.VideoId}";

    string message =
        "🎬 ВЫШЛО НОВОЕ ВИДЕО!\n\n" +
        "На YouTube-канале Forgroup опубликовано новое видео.\n\n" +
        $"📺 {video.Title}\n" +
        $"▶ Смотреть видео: {videoUrl}\n\n" +
        "━━━━━━━━━━━━━━━━━━\n\n" +
        "🎬 NEW VIDEO IS OUT!\n\n" +
        "A new video has been published on the Forgroup YouTube channel.\n\n" +
        $"📺 {video.Title}\n" +
        $"▶ Watch video: {videoUrl}\n\n" +
        "━━━━━━━━━━━━━━━━━━";

    var data = new Dictionary<string, string>
    {
        ["chat_id"] = chatId,
        ["text"] = message
    };

    using var content = new FormUrlEncodedContent(data);

    using var response = await http.PostAsync(
        $"https://api.telegram.org/bot{token}/sendMessage",
        content);

    response.EnsureSuccessStatusCode();

    string result = await response.Content.ReadAsStringAsync();

    using var json = JsonDocument.Parse(result);

    if (!json.RootElement.GetProperty("ok").GetBoolean())
    {
        throw new Exception(
            json.RootElement.TryGetProperty("description", out var error)
                ? error.GetString()
                : "Telegram не принял сообщение.");
    }
}

record Video(string VideoId, string Title);
