using Streamer.bot.Plugin.Interface;
using Streamer.bot.Plugin.Interface.Model;
using Streamer.bot.Plugin.Interface.Enums;
using Streamer.bot.Common.Events;

using System;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NAudio.Wave;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class CPHInline : CPHInlineBase
{
    const string LOG_HEADER = "----- IRL SOUND LOG: ";
    const string DEFAULT_USER = "a user";
    const float SOUND_VOLUME_PERCENT = 50f;
    private LastSpoken lastSpoken = new LastSpoken();
    private SoundAlert lastSpeechFile = new SoundAlert();
    private List<SoundAlert> speechFileQueue = new List<SoundAlert>();
    private List<string> speechFiles = new List<string>();
    private List<string> soundFiles = new List<string>();

    public void Init()
    {
        CPH.RegisterCustomTrigger("Split Speak", "split_speak", new[]{"Speaker.bot Browser Source"});
        GetSoundFiles();
    }

    public void Dispose()
    {
        if (!string.IsNullOrEmpty(lastSpeechFile.FilePath))
            DeleteOldFile(lastSpeechFile.FilePath);
    }

    public bool PrefixLastUser()
    {
        // Get the delay between speaks from arguments or use default
        TimeSpan lastSpokenCooldown = GetLastSpokenCooldown();

        // Get the last user or default user if not set
        string lastUser = lastSpoken.LastUser ?? DEFAULT_USER;

        // Calculate time since the last user spoke
        TimeSpan timeSinceLastSpoken = DateTime.Now - lastSpoken.LastTimeSpoken;

        // Get the current user argument, default to defaultUser if not provided
        if (!CPH.TryGetArg("user", out string user))
            user = DEFAULT_USER;
        user = user.ToLower();

        // Determine if we need to add a message prefix
        if (lastUser != user || timeSinceLastSpoken > lastSpokenCooldown)
            CPH.SetArgument("messagePrefix", $"{user} said ");
        else
            CPH.SetArgument("messagePrefix", string.Empty);

        // Update the last spoken data
        lastSpoken = new LastSpoken
        {
            LastUser = user,
            LastTimeSpoken = DateTime.Now
        };
        return true;
    }

    public bool PlayAlert()
    {
        string methodName = $"{MethodBase.GetCurrentMethod().Name}: ";

        // Default variables
        string soundName;
        float soundVolume;
        bool tts = false;

        // Get %soundId%
        // Set tts to true if %speechFile% exists
        if (!CPH.TryGetArg("speechFile", out string speechFilePath))
        {
            speechFilePath = string.Empty;
            if (!CPH.TryGetArg("soundId", out soundName))
            {
                CPH.LogError($"{LOG_HEADER}{methodName}%soundId% doesn't exist");
                return false;
            }
        }
        else
        {
            tts = true;
            soundName = Path.GetFileName(speechFilePath);
        }
        Log($"{methodName}soundName '{soundName}'");

        // Get %soundVolume%
        if (!CPH.TryGetArg("soundVolume", out string soundVolumeString))
            soundVolumeString = "50";

        if (!int.TryParse(soundVolumeString, out int soundVolumePercent))
            CPH.LogError($"${LOG_HEADER}{methodName}%soundVolume% doesn't exist. Playing at 50% volume");

        Log($"{methodName}Parsed soundVolumePercent: {soundVolumePercent}");
        soundVolume = soundVolumePercent / 100f;
        Log($"{methodName}soundVolumePercent '{soundVolumePercent}%'");
        Log($"{methodName}soundVolume '{soundVolume}%'");

        // Get %duration%, if it exists
        if (!CPH.TryGetArg("duration", out long duration))
        {
            string filePath = GetFilePath(soundName);
            if (File.Exists(filePath))
            {
                Log($"{methodName}File '{filePath}' exists");
                duration = GetSoundDuration(filePath);
            }
            else
            {
                Log($"{methodName}File '{filePath}' does not exist");
                duration = 0;
            }
        }
        Log($"{methodName}duration '{duration}'");

        SoundAlert soundAlert = new SoundAlert
        {
            FilePath = speechFilePath,
            FileName = soundName,
            Volume = soundVolume,
            IsTts = tts,
            Duration = duration
        };

        if (CPH.TryGetArg("cleanedMessage", out string cleanedMessage))
        {
            soundAlert.TtsMessage = cleanedMessage;
            if (CPH.TryGetArg("messagePrefix", out string messagePrefix)){}
                soundAlert.TtsMessage = messagePrefix+cleanedMessage;
            if (CPH.TryGetArg("userName", out string userName))
                soundAlert.TtsUser = userName;
        }

        SendSoundAlertEvent(soundAlert);
        if (tts)
        {
            if (!string.IsNullOrEmpty(lastSpeechFile.FileName))
            {
                Log($"{methodName}lastSpeechFile\u000A{JsonConvert.SerializeObject(lastSpeechFile, Formatting.Indented)}");
                DeleteOldFile(lastSpeechFile.FilePath);
            }
            Log($"{methodName}newSpeechFile\u000A{JsonConvert.SerializeObject(soundAlert, Formatting.Indented)}");
            lastSpeechFile = soundAlert;
        }
        return true;
    }

    public bool ReplayLastTTS()
    {
        string methodName = $"{MethodBase.GetCurrentMethod().Name}: ";
        if (string.IsNullOrEmpty(lastSpeechFile.FilePath))
        {
            CPH.LogError($"{LOG_HEADER}{methodName}There is no TTS to replay");
            return false;
        }

        SendSoundAlertEvent(lastSpeechFile);
        CPH.SetArgument("duration", lastSpeechFile.Duration);
        return true;
    }

    public bool SetVoice()
    {
        string methodName = $"{MethodBase.GetCurrentMethod().Name}: ";
        string message;
        switch (CPH.GetEventType())
        {
            case EventType.TwitchChatMessage:
            case EventType.YouTubeMessage:
            case EventType.TrovoChatMessage:
                if (!CPH.TryGetArg("messageStripped", out message))
                {
                    if (!CPH.TryGetArg("message", out message))
                    {
                        CPH.LogError($"{LOG_HEADER}{methodName}No %message% or %messageStripped% found");
                        return false;
                    }
                }
                break;
            case EventType.CommandTriggered:
            case EventType.TwitchRewardRedemption:
                if (!CPH.TryGetArg("rawInput", out message))
                {
                    CPH.LogError($"{LOG_HEADER}{methodName}No %rawInput% found");
                    return false;
                }
                break;
            default:
                CPH.LogError($"{LOG_HEADER}{methodName}No matching trigger found");
                return false;
        }

        return true;
    }

    private void SeparateVoiceMessage(string userInput)
    {
        List<VoiceMessages> messages = new List<VoiceMessages>();
        MatchCollection matches = Regex.Matches(userInput, @"\(([^)]*)\)");

        foreach (Match match in matches)
        {
            var voiceAlias = "";
            var message = "";
            
            // Check if the input string contains the current match
            int startIndex = userInput.IndexOf(match.Value);
            int endIndex = startIndex + match.Value.Length;

            // Try to find a closing parenthesis after the current match
            int closeParenthesesIndex = userInput.IndexOf(')', endIndex);

            // If a closing parenthesis is found, extract the voice alias and message
            if (closeParenthesesIndex != -1)
            {
                voiceAlias = userInput.Substring(startIndex + 1, closeParenthesesIndex - startIndex - 1);
                message = userInput.Substring(closeParenthesesIndex + 1);
            }
            else
            {
                // If no closing parenthesis is found, use the entire input string as the message
                voiceAlias = "";
                message = userInput;
            }

            messages.Add(new VoiceMessages()
            {
                voiceAlias = voiceAlias,
                message = message
            });
        }

        foreach (var item in messages)
        {
            // args.Add(item.voiceAlias, item.message);
            var eventArgs = new Dictionary<string, object>
            {
                { "voiceAlias", item.voiceAlias },
                { "message", item.message },
            };
            CPH.TriggerCodeEvent("split_speak", true);
        }
    }

    private TimeSpan GetLastSpokenCooldown()
    {
        if (CPH.TryGetArg("delayBetweenSpeaks", out string delayString))
        {
            if (TryParseTimeString(delayString, out TimeSpan cooldown))
            {
                return cooldown;
            }
        }
        return TimeSpan.FromSeconds(20); // Default cooldown 20 seconds
    }

    private void DeleteOldFile(string filePath)
    {
        string methodName = $"{MethodBase.GetCurrentMethod().Name}: ";
        try
        {
            if (!File.Exists(filePath))
                return;
            File.Delete(filePath);
            Log($"{methodName}Deleted old file: '{filePath}'");
        }
        catch (Exception ex)
        {
            CPH.LogError($"{LOG_HEADER}{methodName}Error deleting file '{filePath}': {ex.Message}");
        }
    }

    private List<string> GetSoundFiles()
    {
        string methodName = $"{MethodBase.GetCurrentMethod().Name}: ";
        var soundFiles = new List<string>();
        string directory = CPH.GetGlobalVar<string>("irlSpeakerDirectory") ?? null;
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return soundFiles;
        // soundFiles = Directory.GetFiles(directory).Select(file => Path.GetFullPath(Path.GetFileName(file))).ToList();
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            soundFiles.Add(Path.GetFullPath(file));
        }
        Log($"{methodName}soundFiles:\u000A{JsonConvert.SerializeObject(soundFiles, Formatting.Indented)}");
        return soundFiles;
    }

    private string GetFilePath(string fileName)
    {
        if (soundFiles.Count < 1)
        {
            soundFiles = GetSoundFiles();
            if (soundFiles.Count < 1)
                return null;
        }
        return soundFiles.FirstOrDefault(file => Path.GetFileName(file).Equals(fileName, StringComparison.OrdinalIgnoreCase));
    }

    private long GetSoundDuration(string filePath)
    {
        string methodName = $"{MethodBase.GetCurrentMethod().Name}: ";
        if (string.IsNullOrEmpty(filePath))
        {
            CPH.LogError($"{LOG_HEADER}{methodName}File path cannot be null or empty.");
            return 0;
        }

        try
        {
            long duration = 0;
            string fileExtension = Path.GetExtension(filePath);
            Log($"{methodName}fileExtention: '{fileExtension}'");
            switch (fileExtension)
            {
                case "wav":
                case ".wav":
                    using (var wavReader = new WaveFileReader(filePath))
                    {
                        duration = (long)wavReader.TotalTime.TotalMilliseconds;
                    }
                    break;
                case "mp3":
                case ".mp3":
                    using (var reader = new Mp3FileReader(filePath))
                    {
                        duration = (long)reader.TotalTime.TotalMilliseconds;
                    }
                    break;
                default:
                    using (var soundReader = new AudioFileReader(filePath))
                    {
                        duration = (long)soundReader.TotalTime.TotalMilliseconds;
                    }
                    break;
            }
            Log($"{methodName}File '{filePath}' is '{duration}' milliseconds");
            return duration;
        }
        catch (Exception ex)
        {
            CPH.LogError($"{LOG_HEADER}{methodName}Failed to read file: '{ex.Message}'");
            return 0;
        }
    }

    private void SendSoundAlertEvent(SoundAlert soundAlert)
    {
        string methodName = $"{MethodBase.GetCurrentMethod().Name}: ";
        JObject soundAlertData = new JObject(
            new JProperty("soundId", soundAlert.FileName),
            new JProperty("soundVolume", soundAlert.Volume),
            new JProperty("isTts", soundAlert.IsTts),
            new JProperty("duration", soundAlert.Duration)
        );

        Log($"{methodName}json data:\u000A{soundAlertData.ToString(Formatting.Indented)}");
        CPH.WebsocketBroadcastJson(soundAlertData.ToString(Formatting.None));
    }

    private bool TryParseTimeString(string input, out TimeSpan parsedTime)
    {
        // Remove all whitespaces from the input string
        input = Regex.Replace(input, @"\s+", "");

        // Define the pattern for the input string to match a sequence of digits
        string pattern = @"^\d+$";

        // Match the pattern using Regex
        Match match = Regex.Match(input, pattern);

        if (match.Success)
        {
            // If the input is a sequence of digits, parse it as seconds
            int seconds = int.Parse(input);

            // Construct the TimeSpan with the parsed seconds
            parsedTime = TimeSpan.FromSeconds(seconds);
            return true;
        }

        // Define the pattern for the input string to match days, hours, minutes, and seconds
        pattern = @"(?:(\d+)d)?(?:(\d+)h)?(?:(\d+)m)?(?:(\d+)s)?";

        // Match the pattern using Regex
        match = Regex.Match(input, pattern);

        if (match.Success)
        {
            // Extract values from the matched groups
            int days = match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : 0;
            int hours = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0;
            int minutes = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;
            int seconds = match.Groups[4].Success ? int.Parse(match.Groups[4].Value) : 0;

            // Construct the TimeSpan with the extracted values
            parsedTime = new TimeSpan(days, hours, minutes, seconds);
            return true;
        }

        // If parsing fails, set the out parameter to TimeSpan.Zero and return false
        parsedTime = TimeSpan.Zero;
        return false;
    }

    private void Log(string logMessage)
    {
        CPH.LogVerbose($"{LOG_HEADER}{logMessage}");
    }

    private class SoundAlert
    {
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public float Volume { get; set; }
        public bool IsTts { get; set; }
        public long Duration { get; set; }
        public string TtsUser { get; set; }
        public string TtsMessage { get; set; }
    }

    private class LastSpoken
    {
        public string LastUser { get; set; } = DEFAULT_USER;
        public DateTime LastTimeSpoken { get; set; } = DateTime.MinValue;
    }

    private class VoiceMessages
    {
        public string voiceAlias { get; set; }
        public string message { get; set; }
    }
}
