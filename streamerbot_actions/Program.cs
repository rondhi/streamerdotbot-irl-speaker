/* 
Author: rondhi, https://twitch.tv/rondhi, https://bsky.app/profile/rondhi.bsky.social
                https://x.com/rondhi, https://ko-fi.com/rondhi, https://shop.rondhi.com
Support: https://discord.streamer.bot

This program is licensed under the GNU General Public License Version 3 (GPLv3).

The GPLv3 is a free software license that ensures end users have the freedom to run,
study, share, and modify the software. Key provisions include:

- Copyleft: Modified versions of the software must also be licensed under the GPLv3.
- Source Code: You must provide access to the source code when distributing the software.
- Credit: You must credit the original author of the software, by mentioning either contact e-mail or their social media.
- No Warranty: The software is provided "as-is," without warranty of any kind.

For more details, see https://www.gnu.org/licenses/gpl-3.0.en.html.
*/
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
using System.Text;

public class CPHInline : CPHInlineBase
{
    const string LOG_PREFIX = "IRL SPEAKER LOG: ";
    const string DEFAULT_USER = "a user";
    const string SOUND_VOLUME_PERCENT = "50";
    private LastSpoken lastSpoken = new LastSpoken();
    private SoundAlert lastSpeechFile = new SoundAlert();
    private List<SoundAlert> speechFileQueue = new List<SoundAlert>();
    const float FULL_VOLUME = 1.0f; // Default volume (100%)
    private Dictionary<string, string> soundFilesDict = new Dictionary<string, string>(); // Dictionary to store sound file paths with corresponding filenames without extensions

    public void Init()
    {
        CPH.RegisterCustomTrigger("Split Speak", "split_speak", new[] { "Speaker.bot Browser Source" });

        // Set default global variables
        if (string.IsNullOrEmpty(CPH.GetGlobalVar<string>("irlSpeakerUsingNodeServer")))
            CPH.SetGlobalVar("irlSpeakerUsingNodeServer", false);
        if (string.IsNullOrEmpty(CPH.GetGlobalVar<string>("irlSpeakerSoundDirectory")))
            CPH.SetGlobalVar("irlSpeakerSoundDirectory", Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "sounds")));
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
        // user = user.ToLower();
        user = CapitalizeLetters(user);

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

    public bool PlaySoundRemote()
    {
        string methodName = $"{MethodBase.GetCurrentMethod().Name}: ";

        // Try to get the sound directory from the Set Argument sub-action
        if (!TryBuildSoundDictionary(out soundFilesDict))
            return false;
        LogVerbose($"{methodName}Cound count: {soundFilesDict.Count}");
        // LogVerbose($"{methodName} {JsonConvert.SerializeObject(soundFilesDict, Formatting.None)}");

        // Default variables
        string soundName;
        bool tts = false;

        // Get %soundId%
        // Set tts to true if %speechFile% exists
        if (CPH.TryGetArg("speechFile", out string speechFilePath))
        {
            tts = true;
            soundName = Path.GetFileName(speechFilePath);
        }
        else
        {
            speechFilePath = string.Empty;
            if (!CPH.TryGetArg("soundId", out soundName))
            {
                LogError($"{LOG_PREFIX}{methodName}%soundId% doesn't exist");
                return false;
            }
        }

        // Get %soundVolume%
        if (!CPH.TryGetArg("soundVolume", out string soundVolumePercent))
        {
            soundVolumePercent = SOUND_VOLUME_PERCENT;
            LogError($"${LOG_PREFIX}{methodName}%soundVolume% doesn't exist. Playing at 50% volume");
        }
        LogVerbose($"{methodName}soundVolumePercent '{soundVolumePercent}%'");
        float soundVolume = ParseVolume(soundVolumePercent);
        LogVerbose($"{methodName}soundVolume '{soundVolume}%'");

        // Get %duration%, if it exists (duration assumes it's a speech file)
        if (!CPH.TryGetArg("duration", out long duration))
        {
            // string filePath = GetFilePath(soundName);
            LogVerbose($"{methodName}soundName '{soundName}'");
            string cleanedName = CleanString(soundName);
            LogVerbose($"{methodName}cleaned soundName '{cleanedName}'");
            if (!TryFindSound(CleanString(cleanedName), soundFilesDict, out string filePath))
            {
                LogError($"{methodName}Unable to find matching sound for '{soundName}'");
                return false;
            }
            soundName = filePath;
            if (File.Exists(filePath))
            {
                LogVerbose($"{methodName}File exists '{filePath}'");
                soundName = RemoveOriginalDirectory(soundName).TrimStart('/');
                LogVerbose($"{methodName}originalPathRemoved '{soundName}'");
                duration = GetSoundDuration(filePath);
            }
            else
            {
                LogVerbose($"{methodName}File '{filePath}' does not exist");
                duration = 0;
            }
        }
        LogVerbose($"{methodName}duration '{duration}'");

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

        BroadcastSoundAlert(soundAlert);
        if (tts)
        {
            if (!string.IsNullOrEmpty(lastSpeechFile.FileName))
            {
                LogVerbose($"{methodName}lastSpeechFile\u000A{JsonConvert.SerializeObject(lastSpeechFile, Formatting.None)}");
                DeleteOldFile(lastSpeechFile.FilePath);
            }
            LogVerbose($"{methodName}newSpeechFile\u000A{JsonConvert.SerializeObject(soundAlert, Formatting.None)}");
            lastSpeechFile = soundAlert;
        }
        return true;
    }

    public bool ModifyPermission()
    {
        string methodName = $"{MethodBase.GetCurrentMethod().Name}: ";
        Platform currentPlatform = Platform.Twitch;
        EventType eventType = CPH.GetEventType();
        switch (eventType)
        {
            case EventType.CommandTriggered:
                CPH.TryGetArg("commandSource", out string commandSource);
                if (!Enum.TryParse(commandSource, true, out currentPlatform))
                    LogError($"{LOG_PREFIX}{methodName}Unable to parse commandSource");
                break;
        }
        if (!CPH.TryGetArg("targetUserId", out string userId))
            return false;
        if (!CPH.TryGetArg("groupName", out string groupName))
            groupName = "TTS Permission";
        if (!CPH.TryGetArg("addPermission", out bool addPermission))
            addPermission = false;
        if (addPermission)
            return CPH.AddUserIdToGroup(userId, currentPlatform, groupName);
        else
            return CPH.RemoveUserIdFromGroup(userId, currentPlatform, groupName);
    }

    public bool ReplayLastTTS()
    {
        string methodName = $"{MethodBase.GetCurrentMethod().Name}: ";
        if (string.IsNullOrEmpty(lastSpeechFile.FilePath))
        {
            LogError($"{LOG_PREFIX}{methodName}There is no TTS to replay");
            return false;
        }

        BroadcastSoundAlert(lastSpeechFile);
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
                        LogError($"{LOG_PREFIX}{methodName}No %message% or %messageStripped% found");
                        return false;
                    }
                }
                break;
            case EventType.CommandTriggered:
            case EventType.TwitchRewardRedemption:
                if (!CPH.TryGetArg("rawInput", out message))
                {
                    LogError($"{LOG_PREFIX}{methodName}No %rawInput% found");
                    return false;
                }
                break;
            default:
                LogError($"{LOG_PREFIX}{methodName}No matching trigger found");
                return false;
        }

        return true;
    }

    private string RemoveOriginalDirectory(string fullPath)
    {
        string originalDirectory = CPH.GetGlobalVar<string>("irlSpeakerSoundDirectory");
        if (fullPath.Contains("\\"))
            fullPath = fullPath.Replace("\\", "/");
        if (originalDirectory.Contains("\\"))
            originalDirectory = originalDirectory.Replace("\\", "/");

        int lastIndex = fullPath.LastIndexOf(originalDirectory);
        return fullPath.Substring(lastIndex + originalDirectory.Length);
    }

    private string CleanString(string input)
    {
        return Regex.Replace(input.ToLower(), @"[^a-zA-Z0-9_-]", "_");
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

    public bool DeleteAllSpeechFiles()
    {
        if (!TryBuildSoundDictionary(out var speechFilesDict))
        foreach (var file in speechFilesDict)
        {
            DeleteOldFile(file.Value);
        }
        return true;
    }

    private void DeleteOldFile(string filePath)
    {
        string methodName = $"{MethodBase.GetCurrentMethod().Name}: ";
        try
        {
            if (!File.Exists(filePath))
                return;
            File.Delete(filePath);
            LogVerbose($"{methodName}Deleted old file: '{filePath}'");
        }
        catch (Exception ex)
        {
            LogError($"{LOG_PREFIX}{methodName}Error deleting file '{filePath}': {ex.Message}");
        }
    }

    private long GetSoundDuration(string filePath)
    {
        string methodName = $"{MethodBase.GetCurrentMethod().Name}: ";
        if (string.IsNullOrEmpty(filePath))
        {
            LogError($"{LOG_PREFIX}{methodName}File path cannot be null or empty.");
            return 0;
        }

        try
        {
            long duration = 0;
            string fileExtension = Path.GetExtension(filePath);
            LogVerbose($"{methodName}fileExtention: '{fileExtension}'");
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
            LogVerbose($"{methodName}File '{filePath}' is '{duration}' milliseconds");
            return duration;
        }
        catch (Exception ex)
        {
            LogError($"{LOG_PREFIX}{methodName}Failed to read file: '{ex.Message}'");
            return 0;
        }
    }

    private void BroadcastSoundAlert(SoundAlert soundAlert)
    {
        string methodName = $"{MethodBase.GetCurrentMethod().Name}: ";
        bool usingNodeServer = CPH.GetGlobalVar<bool?>("irlSpeakerUsingNodeServer") ?? false;
        JObject soundAlertData = new JObject(
            new JProperty("extensionName", "irlSpeaker"),
            new JProperty("soundId", soundAlert.FileName),
            new JProperty("soundVolume", soundAlert.Volume),
            new JProperty("isTts", soundAlert.IsTts),
            new JProperty("duration", soundAlert.Duration),
            new JProperty("usingNodeServer", usingNodeServer)
        );

        LogVerbose($"{methodName}json data:\u000A{soundAlertData.ToString(Formatting.None)}");
        CPH.WebsocketBroadcastJson(soundAlertData.ToString(Formatting.None));
    }

    public bool TryBuildSoundDictionary(out Dictionary<string, string> fileDict)
    {
        string methodName = $"{MethodBase.GetCurrentMethod().Name}: ";
        fileDict = new Dictionary<string, string>();

        string directory = CPH.GetGlobalVar<string>("irlSpeakerSoundDirectory");
        if (string.IsNullOrEmpty(directory))
        {
            LogError($"~irlSpeakerSoundDirectory~ is null or empty!");
            return false;
        }

        // Find the sound paths in the specified directory
        if (fileDict.Count < 1 || fileDict == null)
        {
            fileDict = FindSoundPaths(directory);
            // Check if the sound list is not empty or null
            if (fileDict.Count < 1 || fileDict == null)
            {
                LogError($"{methodName}soundFilesDict is null");
                return false; // Return false if the sound list is empty or null
            }
        }

        return true;
    }

    public bool PlaySound()
    {
        string methodName = $"{MethodBase.GetCurrentMethod().Name}: ";

        // Try to get the sound directory from the Set Argument sub-action
        if (!TryBuildSoundDictionary(out soundFilesDict))
            return false;

        bool finishBeforeContinuing = CPH.TryGetArg("finishBeforeContinuing", out bool fBC) ? fBC : false;
        bool volumeArgument = CPH.TryGetArg("volumeArgument", out bool vA) ? vA : false;
        float volume = FULL_VOLUME; // Initialize the volume value to 100%

        // Parse soundName based on trigger
        string soundName = null;
        switch (CPH.GetEventType())
        {
            case EventType.CommandTriggered:
                // Get the "match[1]" argument to specify the sound name
                if (!CPH.TryGetArg("match[1]", out soundName))
                {
                    LogError($"{methodName}%match[1]% doesn't exist!");
                    return false;
                }
                // Check if the "volume" argument is present in the arguments
                if (CPH.TryGetArg("input1", out string input1) && volumeArgument)
                    volume = ParseVolume(input1);
                break;
            case EventType.TwitchRewardRedemption:
                // Get the "rawInput" argument to specify the sound name
                if (!CPH.TryGetArg("rawInput", out string rawInput))
                {
                    LogError($"{methodName}%rawInput% doesn't exist!");
                    return false;
                }
                string[] words = SplitWords(rawInput); // Split rawInput into words
                soundName = words[0];

                // If there is a second word and it's a number, parse it as the volume to play the sound
                if (words.Length > 1)
                    volume = ParseVolume(words[1]);
                CPH.SetArgument("rewardRedemption", true);
                break;
        }

        // Check that soundName actually exists
        if (string.IsNullOrEmpty(soundName))
        {
            LogError($"{methodName}soundName is null");
            return false;
        }

        // Find the sound path for the specified sound name
        // string soundFilePath = FindSound(soundName.Trim().ToLower(), soundFilesDict);
        if (!TryFindSound(soundName, soundFilesDict, out string soundFilePath))
        {
            LogError($"{methodName}Unable to find matching sound for '{soundName}'");
            return false;
        }

        // Check if the sound file is found and play it with the current volume
        if (string.IsNullOrEmpty(soundFilePath))
        {
            LogError($"{methodName}File '{soundName}' not found!");
            return false;
        }

        if (CPH.TryGetArg("volume", out string volumeInput))
            volume = ParseVolume(volumeInput);

        CPH.PlaySound(soundFilePath, volume, finishBeforeContinuing);
        return true;
    }

    private float ParseVolume(string input)
    {
        // Try to parse the user input as a double
        if (!double.TryParse(input, out double userVolume))
            userVolume = 100;
        
        // Clamp the user input value to a range of 1-100%
        if (userVolume < 1)
            userVolume = 1;
        if (userVolume > 100)
            userVolume = 100;

        // Update the volume value based on the parsed and clamped user input
        return (float)userVolume / 100;
    }

    private string[] SplitWords(string input)
    {
        return Regex.Split(input, @"\s+");
    }

    /// <summary>
    /// Finds a sound path in the specified dictionary based on the given sound name.
    /// </summary>
    private bool TryFindSound(string soundName, Dictionary<string, string> soundsHash, out string filePath)
    {
        // Try to get the sound path for the specified sound name
        return soundsHash.TryGetValue(soundName, out filePath);
    }

    /// <summary>
    /// Finds all sound paths in the specified directory and its subdirectories.
    /// </summary>
    private Dictionary<string, string> FindSoundPaths(string soundDirectory)
    {
        string methodName = $"{MethodBase.GetCurrentMethod().Name}: ";
        try
        {
            var sounds = new Dictionary<string, string>();

            // Check if the specified directory exists and has any files
            if (!Directory.Exists(soundDirectory) || !Directory.EnumerateFiles(soundDirectory, "*", SearchOption.AllDirectories).Any())
                return null;

            foreach (string fileName in Directory.EnumerateFiles(soundDirectory, "*", SearchOption.AllDirectories))
            {
                var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
                var soundPath = Path.GetFullPath(fileName);

                // Skip files with extensions other than .wav or .mp3
                if ((Path.GetExtension(fileName) != ".wav" && Path.GetExtension(fileName) != ".mp3") ||
                    sounds.TryGetValue(fileNameWithoutExtension.ToLower(), out _))
                    continue;

                // Add the sound file path to the dictionary
                sounds.Add(fileNameWithoutExtension.ToLower(), soundPath);
            }

            // Log a verbose message with the populated sound dictionary for debugging purposes
            // LogVerbose($"{methodName}{JsonConvert.SerializeObject(sounds, Formatting.None)}");
            return sounds;
        }
        catch (Exception ex)
        {
            LogError($"{methodName}Exception: {ex.Message}");
            return null;
        }
    }

    // Helper method to capitalize letters in a string and add spaces before them if needed
    private static string CapitalizeLetters(string input)
    {
        var sb = new StringBuilder();
        foreach (char c in input)
        {
            if (char.IsLower(c))
                sb.Append(c);
            else
            {
                sb.Append($" {char.ToUpper(c)}");
            }
        }

        return sb.ToString().Trim();
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

    private void LogError(string errorMessage)
    {
        CPH.LogError($"{LOG_PREFIX}{errorMessage}");
    }

    private void LogVerbose(string verboseMessage)
    {
        CPH.LogVerbose($"{LOG_PREFIX}{verboseMessage}");
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
