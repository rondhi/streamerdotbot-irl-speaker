const { StreamerbotClient } = window;
const params = new URLSearchParams(window.location.search); // Get URL parameters from browser

function decodeBase64(base64EncodedStr) {
  return atob(base64EncodedStr);
}

// Function to get boolean value from URL parameter
function getBooleanParam(paramName) {
  const paramValue = params.get(paramName);
	return paramValue === 'true'; // Convert the string to boolean
}

// Streamer.bot Client Connect
const client = new StreamerbotClient({
  scheme: scheme,
  host: host,
  port: port,
  password: password,
  endpoint: endpoint,
  onConnect: async (sbInfo) => {
    const sbName = sbInfo.name;
    console.log(`Connected to Streamer.bot '${sbName}' on ${host}:${port}`);
    SetConnectionStatus(true);
  },
  onDisconnect: async () => {
    SetConnectionStatus(false);
  },
});

///////////////////////////////////
// SRTEAMER.BOT WEBSOCKET SERVER //
///////////////////////////////////

// This is the main function that connects to the Streamer.bot websocket server
function connectSB() {
  client.on('General.Custom', (sbData) => {
    const data = sbData.data;
    // Ignore other General Custom
    if (data.extensionName !== 'irlSpeaker' ||
        data.soundId === undefined ||
        data.soundVolume === undefined ||
        data.isTts === undefined ||
        data.duration === undefined ||
        data.usingNodeServer === undefined) {
      return;
    }
    console.log('Received data from Streamer.bot:', data); // Log the entire received data object
    // const { soundId, soundVolume, tts } = data;
    const soundId = data.soundId;
    const soundVolume = data.soundVolume;
    const isTts = data.isTts;
    const duration = data.duration;
    usingNodeServer = data.usingNodeServer;

    // Example URL: https://example.com/playSound?soundId=abc&soundVolume=0.5&timeout=60
    playSound(soundId, soundVolume, isTts, duration, usingNodeServer);
  });
}

// Fetch sounds from the server and populate the sounds array
function fetchSounds() {
  fetch(soundsApiUrl)
    .then((response) => response.json())
    .then((soundFiles) => {
      sounds = soundFiles.map(
        (soundFile) =>
          `https://${nodeHost}/sounds/${encodeURIComponent(soundFile)}`
      );
      console.log('Sounds loaded:', sounds);
    })
    .catch((error) => console.error('Error fetching sounds:', error));
}

function playSound(soundId, soundVolume, isTts, duration, usingNodeServer) {
  let soundUrl;
  if (isTts) {
    soundUrl = `${soundsTtsUrl}/${soundId}`;
  } else {
    if (usingNodeServer) {
      if (sounds.length === 0) {
        fetchSounds();
      }
      soundUrl = getHighestPrioritySound(soundId, sounds);
    } else {
      soundUrl = `https://${soundsEndpoint}/${soundId}`;
    }
  }

  console.log(`timeoutParam: '${timeoutParam}'`);
  console.log('Attempting to play sound:', soundUrl);

  if (soundUrl) {
    const audio = new Audio(soundUrl);
    audio.volume = soundVolume;

    audio.addEventListener('error', (e) => {
      console.error('Error loading audio:', e);
    });

    // Start playback immediately
    const playPromise = audio.play();

    // Handle playback success and failure
    if (playPromise !== undefined) {
      playPromise
        .then(() => {
          console.log('Audio playback started');

          // Set a timeout to stop playback if duration is too long (only for TTS)
          if (duration > 0) {
            const timeout = timeoutParam * 1000; // Default to 20 seconds for TTS
            const effectiveTimeout = Math.min(timeout, duration); // Cap timeout at duration
            setTimeout(() => {
              audio.pause();
              audio.currentTime = 0;
              console.log(
                `Audio playback stopped after ${
                  effectiveTimeout / 1000
                } seconds`
              );
            }, effectiveTimeout);
          }
        })
        .catch((error) => {
          console.error('Playback failed:', error);
        });
    }

    // Handle natural end of playback
    audio.addEventListener('ended', () => {
      console.log('Audio playback ended');
    });
  } else {
    console.error('Sound file not found:', soundId);
  }
}

//////////////////////
// HELPER FUNCTIONS //
//////////////////////

function getHighestPrioritySound(soundId, soundArray) {
  // Filter sounds to find all matches
  const matchingSounds = soundArray.filter((sound) => sound.endsWith(soundId));

  if (matchingSounds.length === 0) {
    return null;
  }

  // Sort by directory depth (higher directories first)
  matchingSounds.sort((a, b) => {
    const depthA = (a.match(/\//g) || []).length;
    const depthB = (b.match(/\//g) || []).length;
    return depthA - depthB;
  });

  // Return the first (highest priority) match
  return matchingSounds[0];
}

///////////////////////////////////
// STREAMER.BOT WEBSOCKET STATUS //
///////////////////////////////////
// This code originally by Nutty
// This function sets the visibility of the Streamer.bot status label on the overlay
function SetConnectionStatus(connected) {
  let statusContainer = document.getElementById('statusContainer');
  if (connected) {
    statusContainer.style.background = '#2FB774';
    statusContainer.innerText = 'Connected!';
    var tl = new TimelineMax();
    tl.to(statusContainer, 2, { opacity: 0, ease: Linear.easeNone });
  } else {
    statusContainer.style.background = '#D12025';
    statusContainer.innerText = 'Connecting...';
    statusContainer.style.opacity = 1;
  }
}

connectSB();
