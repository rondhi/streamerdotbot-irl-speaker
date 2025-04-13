// server.js

const express = require('express');
const cors = require('cors');
const fs = require('fs');
const path = require('path');

const app = express();
const port = 3000; // Choose a port for your server

// Enable CORS for all origins (you can restrict it as needed)
app.use(cors());

// Serve static files (sounds directory)
app.use('/sounds', express.static(path.join(__dirname, 'sounds'), {
    setHeaders: (res, path) => {
        if (path.endsWith('.mp3')) {
            res.set('Content-Type', 'audio/mpeg');
        } else if (path.endsWith('.wav')) {
            res.set('Content-Type', 'audio/wav');
        } else if (path.endsWith('.ogg')) {
            res.set('Content-Type', 'audio/ogg');
        } else if (path.endsWith('.aac')) {
            res.set('Content-Type', 'audio/aac');
        }
    }
}));

// Middleware to log TTS requests
app.use('/tts', (req, res, next) => {
    console.log(`TTS file requested: ${req.path}`);
    next();
});

// Middleware to log TTS requests
app.use('/documents', (req, res, next) => {
    console.log(`documents file requested: ${req.path}`);
    next();
});

// Serve static files (documents directory)
app.use('/documents', express.static(path.join(__dirname, 'documents'), {
    setHeaders: (res, path) => {}
}), (err) => {
    console.error('Error serving file:', err);
    res.status(404).json({ error: 'File not found' });
});

// Serve static files (tts directory)
app.use('/tts', express.static(path.join(__dirname, 'tts'), {
    setHeaders: (res, path) => {
        if (path.endsWith('.wav')) {
            res.set('Content-Type', 'audio/wav');
        }
    }
}));

// Helper function to filter audio files
const isAudioFile = (filename) => {
    if (!filename) return false;
    const audioExtensions = ['.mp3', '.wav', '.ogg'];
    return audioExtensions.some(ext => filename.endsWith(ext));
}

// Helper function to recursively get audio files from a directory
const getAudioFiles = (dir, baseDir) => {
    let results = [];
    const list = fs.readdirSync(dir);
    list.forEach((file) => {
        const filePath = path.join(dir, file);
        const stat = fs.statSync(filePath);
        if (stat && stat.isDirectory()) {
            results = results.concat(getAudioFiles(filePath, baseDir));
        } else if (isAudioFile(file)) {
            results.push(path.relative(baseDir, filePath));
        }
    });
    return results;
};

// Define API endpoints

// Endpoint to list all sound files
app.get('/api/sounds', (req, res) => {
    try {
        const soundFiles = getAudioFiles(path.join(__dirname, 'sounds'), path.join(__dirname, 'sounds'));
        console.log('Sound files:', soundFiles);
        res.json(soundFiles);
    } catch (err) {
        console.error('Error reading sound directory:', err);
        res.status(500).json({ error: 'Internal server error' });
    }
});

// Endpoint to list all TTS files
app.get('/api/tts', (req, res) => {
    try {
        const ttsFiles = getAudioFiles(path.join(__dirname, 'tts'), path.join(__dirname, 'tts'));
        res.json(ttsFiles); // Send the response
    } catch (err) {
        console.error('Error reading TTS directory:', err);
        res.status(500).json({ error: 'Internal server error' });
    }
});

// Start the server
app.listen(port, () => {
    console.log(`Server running at http://localhost:${port}`);
});
