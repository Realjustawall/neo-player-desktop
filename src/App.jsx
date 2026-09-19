import React, { useEffect, useMemo, useRef, useState } from 'react'
import {
  Clock3, Disc3, FolderPlus, Heart, Home, Library, ListMusic, Music2, Pause, Play,
  Plus, Repeat, Repeat1, Search, Settings, Shuffle, SkipBack, SkipForward,
  SlidersHorizontal, Trash2, Volume1, Volume2, VolumeX, X, ChevronLeft, ChevronRight
} from 'lucide-react'
import { api } from './api'

const themes = [
  { id: 'tangerine', name: 'Tangerine', color: '#ff6a00' },
  { id: 'amber', name: 'Amber', color: '#ff9500' },
  { id: 'burnt', name: 'Burnt Orange', color: '#e45b00' },
  { id: 'sunset', name: 'Sunset', color: '#ff7a1a' },
]

const fmt = (seconds) => {
  if (!Number.isFinite(seconds) || seconds < 0) return '0:00'
  const min = Math.floor(seconds / 60)
  const sec = Math.floor(seconds % 60).toString().padStart(2, '0')
  return `${min}:${sec}`
}

function Cover({ song, size = 48, className = '' }) {
  const [failed, setFailed] = useState(false)
  return <div className={`cover ${className}`} style={{ width: size, height: size }}>{!failed && song?.cover ? <img src={song.cover} alt="" onError={() => setFailed(true)} /> : <Music2 size={Math.max(18, size * .34)} />}</div>
}

function IconButton({ children, active = false, title, onClick, className = '' }) {
  return <button className={`icon-button ${active ? 'active' : ''} ${className}`} title={title} onClick={onClick}>{children}</button>
}

function TrackRow({ song, index, current, playing, onPlay, onFavorite, action }) {
  return <div className={`track-row ${current ? 'current' : ''}`} onDoubleClick={() => onPlay(song)}>
    <div className="track-index"><span className="track-number">{index + 1}</span><button className="row-play" onClick={() => onPlay(song)}>{current && playing ? <Pause size={15}/> : <Play size={15} fill="currentColor"/>}</button></div>
    <div className="track-main"><Cover song={song} size={42}/><div className="ellipsis"><div className="track-title">{song.title}</div><div className="muted small">{song.artist || 'Unknown artist'}</div></div></div>
    <div className="track-album ellipsis muted">{song.album || '—'}</div>
    <div className="track-actions"><IconButton active={song.favorite} onClick={() => onFavorite(song)} title="Like"><Heart size={17} fill={song.favorite ? 'currentColor' : 'none'}/></IconButton>{action}</div>
    <div className="track-duration muted">{fmt(song.duration)}</div>
  </div>
}

function App() {
  const audioRef = useRef(null)
  const [page, setPage] = useState('home')
  const [library, setLibrary] = useState([])
  const [favorites, setFavorites] = useState([])
  const [folders, setFolders] = useState([])
  const [playlists, setPlaylists] = useState([])
  const [selectedPlaylist, setSelectedPlaylist] = useState(null)
  const [queue, setQueue] = useState([])
  const [current, setCurrent] = useState(null)
  const [playing, setPlaying] = useState(false)
  const [position, setPosition] = useState(0)
  const [duration, setDuration] = useState(0)
  const [query, setQuery] = useState('')
  const [settings, setSettings] = useState({ theme: 'tangerine', volume: .82, shuffle: false, repeat: 'off', playbackRate: 1, crossfade: 0 })
  const [queueOpen, setQueueOpen] = useState(false)
  const [settingsOpen, setSettingsOpen] = useState(false)
  const [busy, setBusy] = useState(false)
  const [toast, setToast] = useState('')

  const showToast = (text) => { setToast(text); window.clearTimeout(window.__neoToast); window.__neoToast = window.setTimeout(() => setToast(''), 2600) }
  const refresh = async () => {
    const [songs, favs, pls, dirs, savedQueue, savedSettings] = await Promise.all([api.library(), api.favorites(), api.playlists(), api.folders(), api.queue(), api.settings()])
    setLibrary(songs); setFavorites(favs); setPlaylists(pls); setFolders(dirs); setQueue(savedQueue); setSettings(s => ({ ...s, ...savedSettings }))
  }

  useEffect(() => { refresh().catch(e => showToast(e.message)) }, [])
  useEffect(() => {
    document.documentElement.dataset.theme = settings.theme || 'tangerine'
    const audio = audioRef.current
    if (audio) { audio.volume = Math.max(0, Math.min(1, settings.volume ?? .82)); audio.playbackRate = settings.playbackRate || 1 }
  }, [settings.theme, settings.volume, settings.playbackRate])
  useEffect(() => {
    const audio = audioRef.current
    if (!audio || !current) return
    const expected = `/media/${current.id}`
    if (!audio.src.endsWith(expected)) audio.src = expected
    audio.volume = settings.volume ?? .82; audio.playbackRate = settings.playbackRate || 1
    audio.play().then(() => setPlaying(true)).catch(() => setPlaying(false)); api.history(current.id).catch(() => {})
  }, [current?.id])
  useEffect(() => {
    const audio = audioRef.current
    if (!audio) return
    const time = () => { setPosition(audio.currentTime || 0); setDuration(audio.duration || current?.duration || 0) }
    const ended = () => nextTrack(true); const play = () => setPlaying(true); const pause = () => setPlaying(false)
    audio.addEventListener('timeupdate', time); audio.addEventListener('durationchange', time); audio.addEventListener('ended', ended); audio.addEventListener('play', play); audio.addEventListener('pause', pause)
    return () => { audio.removeEventListener('timeupdate', time); audio.removeEventListener('durationchange', time); audio.removeEventListener('ended', ended); audio.removeEventListener('play', play); audio.removeEventListener('pause', pause) }
  }, [queue, current, settings.shuffle, settings.repeat])
  useEffect(() => {
    const onKey = (e) => { const tag = document.activeElement?.tagName; if (['INPUT', 'TEXTAREA'].includes(tag)) return; if (e.code === 'Space') { e.preventDefault(); togglePlay() }; if (e.ctrlKey && e.code === 'ArrowRight') nextTrack(); if (e.ctrlKey && e.code === 'ArrowLeft') prevTrack() }
    window.addEventListener('keydown', onKey); return () => window.removeEventListener('keydown', onKey)
  })

  const filtered = useMemo(() => { const q = query.trim().toLowerCase(); if (!q) return library; return library.filter(s => [s.title, s.artist, s.album, s.genre].some(x => (x || '').toLowerCase().includes(q))) }, [library, query])
  const albums = useMemo(() => { const map = new Map(); library.forEach(song => { const key = `${song.album || 'Unknown album'}|${song.artist || ''}`; if (!map.has(key)) map.set(key, { name: song.album || 'Unknown album', artist: song.artist || 'Unknown artist', song, count: 0 }); map.get(key).count++ }); return [...map.values()].slice(0, 10) }, [library])
  const persistQueue = async (items) => { setQueue(items); try { await api.setQueue(items.map(x => x.id)) } catch {} }
  const playTrack = (song, context = library) => { if (!song) return; if (!queue.some(x => x.id === song.id) || context !== library) persistQueue(context); setCurrent(song) }
  const togglePlay = () => { const audio = audioRef.current; if (!audio) return; if (!current && library.length) return playTrack(library[0], library); if (audio.paused) audio.play().catch(() => {}); else audio.pause() }
  const nextTrack = (fromEnded = false) => { const audio = audioRef.current; if (settings.repeat === 'one' && fromEnded && audio) { audio.currentTime = 0; audio.play().catch(() => {}); return }; const list = queue.length ? queue : library; if (!list.length) return; let idx = Math.max(0, list.findIndex(x => x.id === current?.id)); if (settings.shuffle && list.length > 1) { let next = idx; while (next === idx) next = Math.floor(Math.random() * list.length); idx = next } else idx += 1; if (idx >= list.length) { if (settings.repeat === 'all') idx = 0; else { setPlaying(false); return } }; setCurrent(list[idx]) }
  const prevTrack = () => { const audio = audioRef.current; if (audio && audio.currentTime > 4) { audio.currentTime = 0; return }; const list = queue.length ? queue : library; if (!list.length) return; let idx = list.findIndex(x => x.id === current?.id) - 1; if (idx < 0) idx = settings.repeat === 'all' ? list.length - 1 : 0; setCurrent(list[idx]) }
  const toggleFavorite = async (song) => { await api.favorite(song.id, !song.favorite); setLibrary(v => v.map(x => x.id === song.id ? { ...x, favorite: !song.favorite } : x)); setFavorites(await api.favorites()); if (current?.id === song.id) setCurrent(c => ({ ...c, favorite: !c.favorite })) }
  const chooseFolder = async () => { let path = null; try { path = await window.pywebview?.api?.pick_folder?.() } catch {}; if (!path) path = window.prompt('Music folder path'); if (!path) return; setBusy(true); try { await api.addFolder(path); const result = await api.scan(); await refresh(); showToast(`Scanned ${result.found} files`) } catch (e) { showToast(e.message) } finally { setBusy(false) } }
  const rescan = async () => { setBusy(true); try { const result = await api.scan(); await refresh(); showToast(`Library updated · ${result.found} files`) } catch (e) { showToast(e.message) } finally { setBusy(false) } }
  const saveSettings = async (patch) => { const next = { ...settings, ...patch }; setSettings(next); try { setSettings(await api.patchSettings(patch)) } catch {} }
  const cycleRepeat = () => saveSettings({ repeat: settings.repeat === 'off' ? 'all' : settings.repeat === 'all' ? 'one' : 'off' })
  const createPlaylist = async () => { const name = window.prompt('Playlist name', 'My playlist'); if (!name) return; const p = await api.createPlaylist(name); setPlaylists(await api.playlists()); setSelectedPlaylist({ ...p, songs: [] }); setPage('playlist') }
  const openPlaylist = async (id) => { const p = await api.playlist(id); setSelectedPlaylist(p); setPage('playlist') }
  const sidebarItem = (id, icon, text) => <button className={`nav-item ${page === id ? 'selected' : ''}`} onClick={() => setPage(id)}>{icon}<span>{text}</span></button>
  const renderTracks = (songs, actionBuilder) => <div className="track-table"><div className="track-head"><span>#</span><span>Title</span><span>Album</span><span></span><span><Clock3 size={15}/></span></div>{songs.map((song, i) => <TrackRow key={song.id} song={song} index={i} current={current?.id === song.id} playing={playing} onPlay={(s) => playTrack(s, songs)} onFavorite={toggleFavorite} action={actionBuilder?.(song)}/>)}</div>

  return <div className="app-shell">
    <audio ref={audioRef} preload="metadata" />
    <aside className="sidebar">
      <div className="brand"><div className="brand-mark"><Disc3 size={23}/></div><div><strong>NEO</strong><span>player</span></div></div>
      <nav className="primary-nav">{sidebarItem('home', <Home size={21}/>, 'Home')}{sidebarItem('search', <Search size={21}/>, 'Search')}{sidebarItem('library', <Library size={21}/>, 'Your Library')}</nav>
      <div className="library-card"><div className="library-title"><div><Library size={20}/><span>Your Library</span></div><IconButton title="Create playlist" onClick={createPlaylist}><Plus size={20}/></IconButton></div><div className="chips"><button onClick={() => setPage('playlists')}>Playlists</button><button onClick={() => setPage('favorites')}>Liked</button><button onClick={() => setPage('library')}>Local</button></div><div className="side-list"><button className="side-entry liked" onClick={() => setPage('favorites')}><div className="side-cover heart-cover"><Heart size={20} fill="currentColor"/></div><div><b>Liked Songs</b><span>Playlist · {favorites.length} songs</span></div></button>{playlists.map(p => <button className="side-entry" key={p.id} onClick={() => openPlaylist(p.id)}><div className="side-cover"><ListMusic size={20}/></div><div><b>{p.name}</b><span>Playlist · {p.count} songs</span></div></button>)}</div></div>
    </aside>
    <main className="content"><header className="topbar"><div className="history-buttons"><IconButton><ChevronLeft size={21}/></IconButton><IconButton><ChevronRight size={21}/></IconButton></div><div className="top-actions">{busy && <span className="sync-dot">Scanning…</span>}<button className="pill secondary" onClick={rescan}><SlidersHorizontal size={16}/> Rescan</button><IconButton title="Settings" onClick={() => setSettingsOpen(true)}><Settings size={20}/></IconButton></div></header>
      <div className="scroll-area">
        {page === 'home' && <><section className="hero orange-glow"><p className="eyebrow">OFFLINE · LOCAL FIRST</p><h1>Your music.<br/>Your machine.</h1><p>Fast local library, playlists and playback without an account or cloud.</p><div className="hero-actions"><button className="primary-cta" onClick={chooseFolder}><FolderPlus size={18}/> Add music</button><button className="ghost-cta" onClick={() => setPage('library')}>Open library</button></div></section><section><div className="section-heading"><h2>Made from your library</h2><button onClick={() => setPage('library')}>Show all</button></div><div className="card-grid">{albums.length ? albums.map((a, i) => <button className="media-card" key={`${a.name}-${i}`} onClick={() => { const list = library.filter(s => s.album === a.name); playTrack(list[0], list) }}><Cover song={a.song} size={168} className="card-cover"/><b>{a.name}</b><span>{a.artist} · {a.count} tracks</span><div className="floating-play"><Play size={22} fill="currentColor"/></div></button>) : <EmptyLibrary onAdd={chooseFolder}/>}</div></section>{!!library.length && <section><div className="section-heading"><h2>Recently found</h2></div>{renderTracks(library.slice(-8).reverse())}</section>}</>}
        {page === 'search' && <section className="page-section"><div className="search-hero"><Search size={24}/><input autoFocus value={query} onChange={e => setQuery(e.target.value)} placeholder="What do you want to listen to?"/></div><div className="section-heading"><h2>{query ? `Results for “${query}”` : 'Browse your local music'}</h2><span className="muted">{filtered.length} tracks</span></div>{renderTracks(filtered)}</section>}
        {page === 'library' && <section className="page-section"><div className="page-title-row"><div><p className="eyebrow">LOCAL FILES</p><h1>Your Library</h1><p className="muted">{library.length} tracks · {albums.length} albums</p></div><button className="primary-cta" onClick={chooseFolder}><FolderPlus size={18}/> Add folder</button></div>{renderTracks(library)}</section>}
        {page === 'favorites' && <section className="page-section playlist-page"><div className="playlist-hero liked-hero"><div className="playlist-art"><Heart size={72} fill="currentColor"/></div><div><p>PLAYLIST</p><h1>Liked Songs</h1><span>{favorites.length} songs</span></div></div>{renderTracks(favorites)}</section>}
        {page === 'playlists' && <section className="page-section"><div className="page-title-row"><div><p className="eyebrow">COLLECTION</p><h1>Playlists</h1></div><button className="primary-cta" onClick={createPlaylist}><Plus size={18}/> New playlist</button></div><div className="card-grid">{playlists.map(p => <button className="media-card playlist-card" key={p.id} onClick={() => openPlaylist(p.id)}><div className="playlist-placeholder"><ListMusic size={52}/></div><b>{p.name}</b><span>{p.count} songs</span></button>)}</div></section>}
        {page === 'playlist' && selectedPlaylist && <section className="page-section playlist-page"><div className="playlist-hero"><div className="playlist-art"><ListMusic size={74}/></div><div><p>PLAYLIST</p><h1>{selectedPlaylist.name}</h1><span>{selectedPlaylist.songs?.length || 0} songs</span><div className="playlist-controls"><button className="round-play" onClick={() => selectedPlaylist.songs?.length && playTrack(selectedPlaylist.songs[0], selectedPlaylist.songs)}><Play size={26} fill="currentColor"/></button><IconButton title="Delete playlist" onClick={async () => { await api.deletePlaylist(selectedPlaylist.id); setPlaylists(await api.playlists()); setPage('playlists') }}><Trash2 size={21}/></IconButton></div></div></div>{renderTracks(selectedPlaylist.songs || [], (song) => <IconButton title="Remove" onClick={async () => { await api.removePlaylistSong(selectedPlaylist.id, song.id); setSelectedPlaylist(await api.playlist(selectedPlaylist.id)); setPlaylists(await api.playlists()) }}><X size={17}/></IconButton>)}</section>}
      </div>
    </main>
    {queueOpen && <aside className="right-panel"><div className="panel-head"><div><b>Queue</b><span>{queue.length} tracks</span></div><IconButton onClick={() => setQueueOpen(false)}><X size={20}/></IconButton></div><div className="queue-list">{queue.map((song, i) => <button key={`${song.id}-${i}`} className={`queue-item ${current?.id === song.id ? 'current' : ''}`} onDoubleClick={() => setCurrent(song)}><Cover song={song} size={44}/><div><b>{song.title}</b><span>{song.artist || 'Unknown artist'}</span></div></button>)}</div></aside>}
    <footer className="player-bar"><div className="now-playing">{current ? <><Cover song={current} size={56}/><div className="ellipsis"><b>{current.title}</b><span>{current.artist || 'Unknown artist'}</span></div><IconButton active={current.favorite} onClick={() => toggleFavorite(current)}><Heart size={18} fill={current.favorite ? 'currentColor' : 'none'}/></IconButton></> : <><div className="empty-cover"><Music2 size={22}/></div><div><b>Nothing playing</b><span>Choose a track</span></div></>}</div><div className="player-center"><div className="transport"><IconButton active={settings.shuffle} onClick={() => saveSettings({ shuffle: !settings.shuffle })}><Shuffle size={18}/></IconButton><IconButton onClick={prevTrack}><SkipBack size={20} fill="currentColor"/></IconButton><button className="main-play" onClick={togglePlay}>{playing ? <Pause size={23} fill="currentColor"/> : <Play size={23} fill="currentColor"/>}</button><IconButton onClick={() => nextTrack()}><SkipForward size={20} fill="currentColor"/></IconButton><IconButton active={settings.repeat !== 'off'} onClick={cycleRepeat}>{settings.repeat === 'one' ? <Repeat1 size={18}/> : <Repeat size={18}/>}</IconButton></div><div className="progress-row"><span>{fmt(position)}</span><input type="range" min="0" max={Math.max(1, duration)} step="0.1" value={Math.min(position, duration || 0)} onChange={e => { const v = Number(e.target.value); if (audioRef.current) audioRef.current.currentTime = v; setPosition(v) }}/><span>{fmt(duration)}</span></div></div><div className="player-right"><IconButton active={queueOpen} onClick={() => setQueueOpen(v => !v)}><ListMusic size={19}/></IconButton><IconButton onClick={() => setSettingsOpen(true)}><SlidersHorizontal size={19}/></IconButton>{settings.volume === 0 ? <VolumeX size={18}/> : settings.volume < .5 ? <Volume1 size={18}/> : <Volume2 size={18}/>}<input className="volume" type="range" min="0" max="1" step="0.01" value={settings.volume} onChange={e => saveSettings({ volume: Number(e.target.value) })}/></div></footer>
    {settingsOpen && <div className="modal-backdrop" onMouseDown={e => e.target === e.currentTarget && setSettingsOpen(false)}><div className="settings-modal"><div className="modal-head"><div><p className="eyebrow">NEO PLAYER</p><h2>Settings</h2></div><IconButton onClick={() => setSettingsOpen(false)}><X size={22}/></IconButton></div><div className="setting-group"><h3>Orange theme</h3><p>Dark interface with orange-only accent presets.</p><div className="theme-grid">{themes.map(t => <button key={t.id} className={`theme-choice ${settings.theme === t.id ? 'selected' : ''}`} onClick={() => saveSettings({ theme: t.id })}><span style={{ background: t.color }}/><b>{t.name}</b></button>)}</div></div><div className="setting-group"><h3>Playback</h3><label><span>Speed <b>{settings.playbackRate}×</b></span><input type="range" min="0.5" max="2" step="0.05" value={settings.playbackRate} onChange={e => saveSettings({ playbackRate: Number(e.target.value) })}/></label><label><span>Default volume <b>{Math.round(settings.volume * 100)}%</b></span><input type="range" min="0" max="1" step="0.01" value={settings.volume} onChange={e => saveSettings({ volume: Number(e.target.value) })}/></label></div><div className="setting-group"><div className="section-heading"><div><h3>Music folders</h3><p>Everything stays on this laptop.</p></div><button className="pill secondary" onClick={chooseFolder}><Plus size={16}/> Add</button></div><div className="folder-list">{folders.map(path => <div key={path}><span className="ellipsis">{path}</span><IconButton onClick={async () => { await api.removeFolder(path); setFolders(await api.folders()) }}><Trash2 size={17}/></IconButton></div>)}</div></div></div></div>}
    {toast && <div className="toast">{toast}</div>}
  </div>
}

function EmptyLibrary({ onAdd }) {
  return <div className="empty-library"><div className="empty-icon"><Music2 size={42}/></div><h3>Your library is empty</h3><p>Add a music folder from this PC. NEO indexes files locally.</p><button className="primary-cta" onClick={onAdd}><FolderPlus size={18}/> Choose folder</button></div>
}

export default App
