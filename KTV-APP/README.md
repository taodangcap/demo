# Karaoke App 🎤

A beautiful, modern karaoke web application built with React, featuring real-time lyrics, playlists, favorites, and more.

## Features

- 🎵 **Song Library**: Browse and search through a collection of karaoke songs
- 🎥 **YouTube Video Player**: Search and play YouTube videos with iframe integration
- 🎤 **Real-time Lyrics**: Sing along with synchronized lyrics display
- ❤️ **Favorites**: Save your favorite songs for quick access
- 📝 **Playlists**: Create and manage custom playlists
- ⭐ **Ratings & Reviews**: Rate songs and leave comments
- 🎚️ **Audio Controls**: Full-featured audio player with volume and microphone controls
- 📤 **Song Upload**: Upload your own karaoke tracks
- 🌙 **Dark Mode**: Beautiful dark theme support
- 📱 **Responsive Design**: Works seamlessly on desktop and mobile devices
- 🔐 **User Authentication**: Login system to save preferences

## Tech Stack

- **React 18** - UI library
- **Vite** - Build tool and dev server
- **React Router** - Client-side routing
- **Tailwind CSS** - Utility-first CSS framework
- **Lucide React** - Icon library
- **Context API** - State management

## Getting Started

### Prerequisites

- Node.js 16+ and npm/yarn/pnpm

### Installation

1. Clone the repository:
```bash
git clone <repository-url>
cd karaoke-app
```

2. Install dependencies:
```bash
npm install
```

3. Set up Environment Variables (required for Video Player):
   - Get your YouTube API key from [Google Cloud Console](https://console.cloud.google.com/apis/credentials)
   - Enable YouTube Data API v3 in your project
   - (Optional) Get SoundCloud Client ID from [SoundCloud Developers](https://developers.soundcloud.com/)
   - Create a `.env` file in the root directory:
   ```bash
   VITE_YOUTUBE_API_KEY=your_youtube_api_key_here
   VITE_SOUNDCLOUD_CLIENT_ID=your_soundcloud_client_id_here
   ```
   - See `ENV_SETUP.md` for detailed setup instructions

4. Start the development server:
```bash
npm run dev
```

5. Open your browser and navigate to `http://localhost:3000`

### Build for Production

```bash
npm run build
```

The production build will be in the `dist` directory.

### Preview Production Build

```bash
npm run preview
```

## Project Structure

```
src/
├── components/       # Reusable UI components
│   ├── AudioPlayer.jsx
│   ├── CreatePlaylist.jsx
│   ├── Footer.jsx
│   ├── Header.jsx
│   ├── Layout.jsx
│   ├── LyricsDisplay.jsx
│   ├── Navigation.jsx
│   ├── SongCard.jsx
│   ├── SongRating.jsx
│   └── SongUpload.jsx
├── contexts/         # React Context providers
│   ├── AudioContext.jsx
│   └── AuthContext.jsx
├── pages/            # Page components
│   ├── Home.jsx
│   ├── Login.jsx
│   ├── PlaylistDetail.jsx
│   ├── Profile.jsx
│   ├── Settings.jsx
│   ├── SongLibrary.jsx
│   └── VideoPlayer.jsx
├── utils/            # Utility functions
│   ├── mockData.js
│   ├── storage.js
│   └── youtube.js
├── App.jsx           # Main app component
├── main.jsx          # Entry point
└── index.css         # Global styles
```

## Features in Detail

### Song Library
- Search songs by title or artist
- Filter by genre
- View song details and ratings
- Play songs directly from the library

### Audio Player
- Play/pause controls
- Seek through songs
- Volume control for music
- Microphone volume control
- Progress bar with time display

### Lyrics Display
- Real-time synchronized lyrics
- Highlights current line
- Smooth scrolling to current position

### User Features
- **Favorites**: Click the heart icon on any song to add it to favorites
- **Playlists**: Create custom playlists from the Profile page
- **Ratings**: Rate songs and leave comments
- **Upload**: Upload your own karaoke tracks with lyrics

### YouTube Video Player
- Search YouTube videos using YouTube Data API
- Play videos in an embedded iframe player
- Save selected videos to a playlist
- View watch history
- Karaoke mode toggle
- Simple, dark-themed UI

### Settings
- Toggle dark/light mode
- Adjust audio volume
- Configure microphone settings
- Notification preferences

## Data Storage

The app uses browser localStorage to persist:
- User authentication
- Favorite songs
- Playlists
- Song ratings and comments
- Theme preferences
- Selected YouTube videos
- YouTube watch history

## Deployment

### Vercel

1. Install Vercel CLI:
```bash
npm i -g vercel
```

2. Deploy:
```bash
vercel
```

### Netlify

1. Install Netlify CLI:
```bash
npm i -g netlify-cli
```

2. Build the project:
```bash
npm run build
```

3. Deploy:
```bash
netlify deploy --prod --dir=dist
```

### Manual Deployment

1. Build the project:
```bash
npm run build
```

2. Upload the `dist` folder to your hosting provider

## Browser Support

- Chrome (latest)
- Firefox (latest)
- Safari (latest)
- Edge (latest)

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

MIT License - feel free to use this project for your own purposes.

## Acknowledgments

- Inspired by [vikara.vercel.app](https://vikara.vercel.app/karaoke)
- Icons by [Lucide](https://lucide.dev)
- UI design with [Tailwind CSS](https://tailwindcss.com)

