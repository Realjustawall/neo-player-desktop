import React, { useEffect, useMemo, useRef, useState } from 'react'
import {
  Album, ChevronLeft, ChevronRight, Clock3, Disc3, FolderPlus, Heart, Home, Library,
  ListMusic, Menu, MoreHorizontal, Music2, Pause, Play, Plus, Repeat, Repeat1, Search,
  Settings, Shuffle, SkipBack, SkipForward, SlidersHorizontal, Trash2, UserRound,
  Volume1, Volume2, VolumeX, X
} from 'lucide-react'
import { api } from './api'

const ORANGE_THEMES = [
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

const compareText = (a, b) => String(a || '').localeCompare(String(b || ''), undefined, { sensitivity: 'base', numeric: true })

function IconButton({ children, active = false, title = '', onClick, className = '' }) {
  return <button className={`icon-button ${active ? 'active' : ''} ${className}`} title={title} onClick={onClick}>{children}</button>
}

function Cover({ song, size = 48, className = '' }) {
  const [failed, setFailed] = useState(false)
  useEffect(() => setFailed(false), [song?.id])
  return <div className={`cover ${className}`} style={{ width: size, height: size }}>
    {!failed && song?.cover ? <img src={song.cover} alt="" onError={() => setFailed(true)} /> : <Music2 size={Math.max(18, size * .34)} />}
  </div>
}

function TrackRow({ song, index, current, playing, onPlay, onFavorite, onQueue, onPlaylist, extraAction }) {
  return <div className={`track-row ${current ? 'current' : ''}`} onDoubleClick={() => onPlay(song)}>
    <div className="track-index">
      <span className="track-number">{index + 1}</span>
      <button className="row-play" onClick={() => onPlay(song)}>{current && playing ? <Pause size={15}/> : <Play size={15} fill="currentColor"/>}</button>
    </div>
    <div className="track-main">
      <Cover song={song} size={42}/>
      <div className="ellipsis"><div className="track-title">{song.title}</div><div className="muted small">{song.artist || 'Unknown artist'}</div></div>
    </div>
    <div className="track-album ellipsis muted">{song.album || '—'}</div>
    <div className="track-actions row-actions-wide">
      <IconButton active={song.favorite} onClick={() => onFavorite(song)} title="Like"><Heart size={16} fill={song.favorite ? 'currentColor' : 'none'}/></IconButton>
      <IconButton onClick={() => onQueue(song)} title="Add to queue"><ListMusic size={16}/></IconButton>
      <IconButton onClick={() => onPlaylist(song)} title="Add to playlist"><Plus size={16}/></IconButton>
      {extraAction}
    </div>
    <div className="track-duration muted">{fmt(song.duration)}</div>
  </div>
}

function EntityCard({ title, subtitle, song, icon, onClick, round = false }) {
  return <button className="media-card entity-card" onClick={onClick}>
    {song ? <Cover song={song} size={168} className={`card-cover ${round ? 'artist-cover' : ''}`}/> : <div className={`playlist-placeholder ${round ? 'artist-cover' : ''}`}>{icon}</div>}
    <b>{title}</b><span>{subtitle}</span><div className="floating-play"><Play size={22} fill="currentColor"/></div>
  </button>
}

export default function PlayerApp() {
  const audioRef = useRef(null)
  const [page, setPage] = useState('home')
  const [libraryMode, setLibraryMode] = useState('tracks')
  const [library, setLibrary] = useState([])
  const [favorites, setFavorites] = useState([])
  const [folders, setFolders] = useState([])
  const [playlists, setPlaylists] = useState([])
  const [selectedPlaylist, setSelectedPlaylist] = useState(null)
  const [selectedAlbum, setSelectedAlbum] = useState(null)
  const [selectedArtist, setSelectedArtist] = useState(null)
  const [queue, setQueue] = useState([])
  const [current, setCurrent] = useState(null)
  const [playing, setPlaying] = useState(false)
  const [position, setPosition] = useState(0)
  const [duration, setDuration] = useState(0)
  const [query, setQuery] = useState('')
  const [sort, setSort] = useState('artist')
  const [settings, setSettings] = useState({ theme: 'tangerine', volume: .82, shuffle: false, repeat: 'off', playbackRate: 1 })
  const [queueOpen, setQueueOpen] = useState(false)
  const [settingsOpen, setSettingsOpen] = useState(false)
  const [playlistPickerSong, setPlaylistPickerSong] = useState(null)
  const [busy, setBusy] = useState(false)
  const [toast, setToast] = useState('')

  const showToast = (text) => {
    setToast(text)
    window.clearTimeout(window.__neoToast)
    window.__neoToast = window.setTimeout(() => setToast(''), 2500)
  }

  const refresh = async () => {
    const [songs, favs, pls, dirs, savedQueue, savedSettings] = await Promise.all([
      api.library(), api.favorites(), api.playlists(), api.folders(), api.queue(), api.settings()
    ])
    setLibrary(songs); setFavorites(favs); setPlaylists(pls); setFolders(dirs); setQueue(savedQueue)
    setSettings(s => ({ ...s, ...savedSettings }))
  }

  useEffect(() => { refresh().catch(e => showToast(e.message)) }, [])

  useEffect(() => {
    document.documentElement.dataset.theme = settings.theme || 'tangerine'
    const audio = audioRef.current
    if (audio) {
      audio.volume = Math.max(0, Math.min(1, Number(settings.volume ?? .82)))
      audio.playbackRate = Number(settings.playbackRate || 1)
    }
  }, [settings.theme, settings.volume, settings.playbackRate])

  useEffect(() => {
    const audio = audioRef.current
    if (!audio || !current) return
    const expected = `/media/${current.id}`
    if (!audio.src.endsWith(expected)) audio.src = expected
    audio.volume = Math.max(0, Math.min(1, Number(settings.volume ?? .82)))
    audio.playbackRate = Number(settings.playbackRate || 1)
    audio.play().then(() => setPlaying(true)).catch(() => setPlaying(false))
    api.history(current.id).catch(() => {})
  }, [current?.id])

  useEffect(() => {
    const audio = audioRef.current
    if (!audio) return
    const update = () => { setPosition(audio.currentTime || 0); setDuration(audio.duration || current?.duration || 0) }
    const ended = () => nextTrack(true)
    const onPlay = () => setPlaying(true)
    const onPause = () => setPlaying(false)
    audio.addEventListener('timeupdate', update); audio.addEventListener('durationchange', update)
    audio.addEventListener('ended', ended); audio.addEventListener('play', onPlay); audio.addEventListener('pause', onPause)
    return () => {
      audio.removeEventListener('timeupdate', update); audio.removeEventListener('durationchange', update)
      audio.removeEventListener('ended', ended); audio.removeEventListener('play', onPlay); audio.removeEventListener('pause', onPause)
    }
  }, [queue, current, settings.shuffle, settings.repeat])

  useEffect(() => {
    const onKey = (e) => {
      const tag = document.activeElement?.tagName
      if (['INPUT', 'TEXTAREA', 'SELECT'].includes(tag)) return
      if (e.code === 'Space') { e.preventDefault(); togglePlay() }
      if (e.ctrlKey && e.code === 'ArrowRight') nextTrack()
      if (e.ctrlKey && e.code === 'ArrowLeft') prevTrack()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  })

  const albums = useMemo(() => {
    const map = new Map()
    library.forEach(song => {
      const name = song.album || 'Unknown album'; const artist = song.artist || 'Unknown artist'; const key = `${name}\u0000${artist}`
      if (!map.has(key)) map.set(key, { key, name, artist, song, songs: [] })
      map.get(key).songs.push(song)
    })
    return [...map.values()].sort((a, b) => compareText(a.name, b.name))
  }, [library])

  const artists = useMemo(() => {
    const map = new Map()
    library.forEach(song => {
      const name = song.artist || 'Unknown artist'
      if (!map.has(name)) map.set(name, { name, song, songs: [] })
      map.get(name).songs.push(song)
    })
    return [...map.values()].sort((a, b) => compareText(a.name, b.name))
  }, [library])

  const sortedLibrary = useMemo(() => {
    const items = [...library]
    if (sort === 'title') items.sort((a, b) => compareText(a.title, b.title))
    else if (sort === 'album') items.sort((a, b) => compareText(a.album, b.album) || a.track - b.track || compareText(a.title, b.title))
    else if (sort === 'added') items.sort((a, b) => (b.addedAt || 0) - (a.addedAt || 0))
    else items.sort((a, b) => compareText(a.artist, b.artist) || compareText(a.album, b.album) || a.track - b.track)
    return items
  }, [library, sort])

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase()
    if (!q) return sortedLibrary
    return sortedLibrary.filter(s => [s.title, s.artist, s.album, s.genre].some(x => String(x || '').toLowerCase().includes(q)))
  }, [sortedLibrary, query])

  const persistQueue = async (items) => {
    setQueue(items)
    try { await api.setQueue(items.map(x => x.id)) } catch {}
  }

  const playTrack = (song, context = library) => {
    if (!song) return
    const normalized = context.length ? context : [song]
    persistQueue(normalized)
    setCurrent(song)
  }

  const togglePlay = () => {
    const audio = audioRef.current
    if (!audio) return
    if (!current && library.length) return playTrack(library[0], library)
    if (audio.paused) audio.play().catch(() => {})
    else audio.pause()
  }

  const nextTrack = (fromEnded = false) => {
    const audio = audioRef.current
    if (settings.repeat === 'one' && fromEnded && audio) { audio.currentTime = 0; audio.play().catch(() => {}); return }
    const list = queue.length ? queue : library
    if (!list.length) return
    let idx = Math.max(0, list.findIndex(x => x.id === current?.id))
    if (settings.shuffle && list.length > 1) {
      let next = idx
      while (next === idx) next = Math.floor(Math.random() * list.length)
      idx = next
    } else idx += 1
    if (idx >= list.length) {
      if (settings.repeat === 'all') idx = 0
      else { setPlaying(false); return }
    }
    setCurrent(list[idx])
  }

  const prevTrack = () => {
    const audio = audioRef.current
    if (audio && audio.currentTime > 4) { audio.currentTime = 0; return }
    const list = queue.length ? queue : library
    if (!list.length) return
    let idx = list.findIndex(x => x.id === current?.id) - 1
    if (idx < 0) idx = settings.repeat === 'all' ? list.length - 1 : 0
    setCurrent(list[idx])
  }

  const toggleFavorite = async (song) => {
    const value = !song.favorite
    await api.favorite(song.id, value)
    setLibrary(v => v.map(x => x.id === song.id ? { ...x, favorite: value } : x))
    setFavorites(await api.favorites())
    setQueue(v => v.map(x => x.id === song.id ? { ...x, favorite: value } : x))
    if (current?.id === song.id) setCurrent(c => ({ ...c, favorite: value }))
  }

  const addToQueue = async (song) => {
    const items = [...queue, song]
    await persistQueue(items)
    showToast(`Added “${song.title}” to queue`)
  }

  const removeQueueAt = async (index) => {
    const items = queue.filter((_, i) => i !== index)
    await persistQueue(items)
  }

  const clearQueue = async () => { await persistQueue([]); showToast('Queue cleared') }

  const chooseFolder = async () => {
    let path = null
    try { path = await window.pywebview?.api?.pick_folder?.() } catch {}
    if (!path) path = window.prompt('Music folder path')
    if (!path) return
    setBusy(true)
    try {
      await api.addFolder(path)
      const result = await api.scan()
      await refresh()
      showToast(`Scanned ${result.found} files`)
    } catch (e) { showToast(e.message) } finally { setBusy(false) }
  }

  const rescan = async () => {
    setBusy(true)
    try { const result = await api.scan(); await refresh(); showToast(`Library updated · ${result.found} files`) }
    catch (e) { showToast(e.message) } finally { setBusy(false) }
  }

  const saveSettings = async (patch) => {
    setSettings(s => ({ ...s, ...patch }))
    try { setSettings(await api.patchSettings(patch)) } catch {}
  }

  const cycleRepeat = () => saveSettings({ repeat: settings.repeat === 'off' ? 'all' : settings.repeat === 'all' ? 'one' : 'off' })

  const createPlaylist = async () => {
    const name = window.prompt('Playlist name', 'My playlist')
    if (!name?.trim()) return
    const p = await api.createPlaylist(name.trim())
    setPlaylists(await api.playlists()); setSelectedPlaylist({ ...p, songs: [] }); setPage('playlist')
  }

  const openPlaylist = async (id) => { setSelectedPlaylist(await api.playlist(id)); setPage('playlist') }

  const addSongToPlaylist = async (playlist) => {
    if (!playlistPickerSong) return
    await api.addPlaylistSong(playlist.id, playlistPickerSong.id)
    setPlaylists(await api.playlists())
    if (selectedPlaylist?.id === playlist.id) setSelectedPlaylist(await api.playlist(playlist.id))
    showToast(`Added to ${playlist.name}`)
    setPlaylistPickerSong(null)
  }

  const openAlbum = (album) => { setSelectedAlbum(album); setPage('album') }
  const openArtist = (artist) => { setSelectedArtist(artist); setPage('artist') }

  const sidebarItem = (id, icon, text) => <button className={`nav-item ${page === id ? 'selected' : ''}`} onClick={() => setPage(id)}>{icon}<span>{text}</span></button>

  const renderTracks = (songs, extraActionBuilder) => <div className="track-table enhanced-table">
    <div className="track-head"><span>#</span><span>Title</span><span>Album</span><span></span><span><Clock3 size={15}/></span></div>
    {songs.map((song, i) => <TrackRow key={`${song.id}-${i}`} song={song} index={i} current={current?.id === song.id} playing={playing}
      onPlay={(s) => playTrack(s, songs)} onFavorite={toggleFavorite} onQueue={addToQueue} onPlaylist={setPlaylistPickerSong} extraAction={extraActionBuilder?.(song)} />)}
  </div>

  const libraryToolbar = <div className="library-toolbar">
    <div className="chips big-chips">
      <button className={libraryMode === 'tracks' ? 'selected' : ''} onClick={() => setLibraryMode('tracks')}>Tracks</button>
      <button className={libraryMode === 'albums' ? 'selected' : ''} onClick={() => setLibraryMode('albums')}>Albums</button>
      <button className={libraryMode === 'artists' ? 'selected' : ''} onClick={() => setLibraryMode('artists')}>Artists</button>
    </div>
    {libraryMode === 'tracks' && <select className="sort-select" value={sort} onChange={e => setSort(e.target.value)}>
      <option value="artist">Artist</option><option value="title">Title</option><option value="album">Album</option><option value="added">Recently added</option>
    </select>}
  </div>

  return <div className="app-shell">
    <audio ref={audioRef} preload="metadata" />
    <aside className="sidebar">
      <div className="brand"><div className="brand-mark"><Disc3 size={23}/></div><div><strong>NEO</strong><span>player</span></div></div>
      <nav className="primary-nav">
        {sidebarItem('home', <Home size={21}/>, 'Home')}
        {sidebarItem('search', <Search size={21}/>, 'Search')}
        {sidebarItem('library', <Library size={21}/>, 'Your Library')}
      </nav>
      <div className="library-card">
        <div className="library-title"><div><Library size={20}/><span>Your Library</span></div><IconButton title="Create playlist" onClick={createPlaylist}><Plus size={20}/></IconButton></div>
        <div className="chips"><button onClick={() => setPage('playlists')}>Playlists</button><button onClick={() => setPage('favorites')}>Liked</button><button onClick={() => { setPage('library'); setLibraryMode('albums') }}>Albums</button></div>
        <div className="side-list">
          <button className="side-entry liked" onClick={() => setPage('favorites')}><div className="side-cover heart-cover"><Heart size={20} fill="currentColor"/></div><div><b>Liked Songs</b><span>Playlist · {favorites.length} songs</span></div></button>
          {playlists.map(p => <button className="side-entry" key={p.id} onClick={() => openPlaylist(p.id)}><div className="side-cover"><ListMusic size={20}/></div><div><b>{p.name}</b><span>Playlist · {p.count} songs</span></div></button>)}
        </div>
      </div>
    </aside>

    <main className="content">
      <header className="topbar">
        <div className="history-buttons"><IconButton><ChevronLeft size={21}/></IconButton><IconButton><ChevronRight size={21}/></IconButton></div>
        <div className="top-actions">{busy && <span className="sync-dot">Scanning…</span>}<button className="pill secondary" onClick={rescan}><SlidersHorizontal size={16}/> Rescan</button><IconButton title="Settings" onClick={() => setSettingsOpen(true)}><Settings size={20}/></IconButton></div>
      </header>

      <div className="scroll-area">
        {page === 'home' && <>
          <section className="hero orange-glow"><p className="eyebrow">OFFLINE · LOCAL FIRST</p><h1>Your music.<br/>Your machine.</h1><p>Local library, playlists, queue and playback without an account or cloud.</p><div className="hero-actions"><button className="primary-cta" onClick={chooseFolder}><FolderPlus size={18}/> Add music</button><button className="ghost-cta" onClick={() => setPage('library')}>Open library</button></div></section>
          <section><div className="section-heading"><h2>Albums in your library</h2><button onClick={() => { setPage('library'); setLibraryMode('albums') }}>Show all</button></div><div className="card-grid">{albums.slice(0, 10).map(a => <EntityCard key={a.key} title={a.name} subtitle={`${a.artist} · ${a.songs.length} tracks`} song={a.song} onClick={() => openAlbum(a)}/>)}{!albums.length && <EmptyLibrary onAdd={chooseFolder}/>}</div></section>
          {!!artists.length && <section><div className="section-heading"><h2>Your artists</h2><button onClick={() => { setPage('library'); setLibraryMode('artists') }}>Show all</button></div><div className="card-grid">{artists.slice(0, 8).map(a => <EntityCard key={a.name} title={a.name} subtitle={`${a.songs.length} tracks`} song={a.song} round onClick={() => openArtist(a)}/>)}</div></section>}
          {!!library.length && <section><div className="section-heading"><h2>Recently added</h2></div>{renderTracks([...library].sort((a,b)=>(b.addedAt||0)-(a.addedAt||0)).slice(0,8))}</section>}
        </>}

        {page === 'search' && <section className="page-section">
          <div className="search-hero"><Search size={24}/><input autoFocus value={query} onChange={e => setQuery(e.target.value)} placeholder="What do you want to listen to?"/></div>
          <div className="section-heading"><h2>{query ? `Results for “${query}”` : 'Browse your local music'}</h2><span className="muted">{filtered.length} tracks</span></div>
          {renderTracks(filtered)}
        </section>}

        {page === 'library' && <section className="page-section">
          <div className="page-title-row"><div><p className="eyebrow">LOCAL FILES</p><h1>Your Library</h1><p className="muted">{library.length} tracks · {albums.length} albums · {artists.length} artists</p></div><button className="primary-cta" onClick={chooseFolder}><FolderPlus size={18}/> Add folder</button></div>
          {libraryToolbar}
          {libraryMode === 'tracks' && renderTracks(sortedLibrary)}
          {libraryMode === 'albums' && <div className="card-grid">{albums.map(a => <EntityCard key={a.key} title={a.name} subtitle={`${a.artist} · ${a.songs.length} tracks`} song={a.song} onClick={() => openAlbum(a)}/>)}</div>}
          {libraryMode === 'artists' && <div className="card-grid">{artists.map(a => <EntityCard key={a.name} title={a.name} subtitle={`${a.songs.length} tracks`} song={a.song} round onClick={() => openArtist(a)}/>)}</div>}
        </section>}

        {page === 'favorites' && <section className="page-section playlist-page"><CollectionHero icon={<Heart size={72} fill="currentColor"/>} type="PLAYLIST" title="Liked Songs" subtitle={`${favorites.length} songs`} onPlay={() => favorites.length && playTrack(favorites[0], favorites)}/>{renderTracks(favorites)}</section>}

        {page === 'playlists' && <section className="page-section"><div className="page-title-row"><div><p className="eyebrow">COLLECTION</p><h1>Playlists</h1></div><button className="primary-cta" onClick={createPlaylist}><Plus size={18}/> New playlist</button></div><div className="card-grid">{playlists.map(p => <EntityCard key={p.id} title={p.name} subtitle={`${p.count} songs`} icon={<ListMusic size={52}/>} onClick={() => openPlaylist(p.id)}/>)}</div></section>}

        {page === 'playlist' && selectedPlaylist && <section className="page-section playlist-page">
          <CollectionHero icon={<ListMusic size={72}/>} type="PLAYLIST" title={selectedPlaylist.name} subtitle={`${selectedPlaylist.songs?.length || 0} songs`} onPlay={() => selectedPlaylist.songs?.length && playTrack(selectedPlaylist.songs[0], selectedPlaylist.songs)} actions={<IconButton title="Delete playlist" onClick={async () => { await api.deletePlaylist(selectedPlaylist.id); setPlaylists(await api.playlists()); setPage('playlists') }}><Trash2 size={20}/></IconButton>}/>
          {renderTracks(selectedPlaylist.songs || [], song => <IconButton title="Remove from playlist" onClick={async () => { await api.removePlaylistSong(selectedPlaylist.id, song.id); setSelectedPlaylist(await api.playlist(selectedPlaylist.id)); setPlaylists(await api.playlists()) }}><X size={16}/></IconButton>)}
        </section>}

        {page === 'album' && selectedAlbum && <section className="page-section playlist-page">
          <CollectionHero song={selectedAlbum.song} type="ALBUM" title={selectedAlbum.name} subtitle={`${selectedAlbum.artist} · ${selectedAlbum.songs.length} tracks`} onPlay={() => playTrack(selectedAlbum.songs[0], selectedAlbum.songs)}/>
          {renderTracks(selectedAlbum.songs)}
        </section>}

        {page === 'artist' && selectedArtist && <section className="page-section playlist-page">
          <CollectionHero song={selectedArtist.song} round type="ARTIST" title={selectedArtist.name} subtitle={`${selectedArtist.songs.length} local tracks`} onPlay={() => playTrack(selectedArtist.songs[0], selectedArtist.songs)}/>
          <div className="section-heading artist-section-title"><h2>Popular in your library</h2></div>{renderTracks(selectedArtist.songs)}
          <div className="section-heading artist-section-title"><h2>Albums</h2></div><div className="card-grid">{albums.filter(a => a.artist === selectedArtist.name).map(a => <EntityCard key={a.key} title={a.name} subtitle={`${a.songs.length} tracks`} song={a.song} onClick={() => openAlbum(a)}/>)}</div>
        </section>}
      </div>
    </main>

    {queueOpen && <aside className="right-panel">
      <div className="panel-head"><div><b>Queue</b><span>{queue.length} tracks</span></div><div className="queue-head-actions">{!!queue.length && <button className="text-button" onClick={clearQueue}>Clear</button>}<IconButton onClick={() => setQueueOpen(false)}><X size={20}/></IconButton></div></div>
      <div className="queue-list">{queue.map((song, i) => <div key={`${song.id}-${i}`} className={`queue-item queue-item-editable ${current?.id === song.id ? 'current' : ''}`}><button className="queue-main" onDoubleClick={() => setCurrent(song)}><Cover song={song} size={44}/><div><b>{song.title}</b><span>{song.artist || 'Unknown artist'}</span></div></button><IconButton title="Remove from queue" onClick={() => removeQueueAt(i)}><X size={15}/></IconButton></div>)}</div>
    </aside>}

    <footer className="player-bar">
      <div className="now-playing">{current ? <><Cover song={current} size={56}/><div className="ellipsis"><b>{current.title}</b><button className="artist-link" onClick={() => { const a = artists.find(x => x.name === (current.artist || 'Unknown artist')); if (a) openArtist(a) }}>{current.artist || 'Unknown artist'}</button></div><IconButton active={current.favorite} onClick={() => toggleFavorite(current)}><Heart size={18} fill={current.favorite ? 'currentColor' : 'none'}/></IconButton></> : <><div className="empty-cover"><Music2 size={22}/></div><div><b>Nothing playing</b><span>Choose a track</span></div></>}</div>
      <div className="player-center"><div className="transport"><IconButton active={settings.shuffle} onClick={() => saveSettings({ shuffle: !settings.shuffle })}><Shuffle size={18}/></IconButton><IconButton onClick={prevTrack}><SkipBack size={20} fill="currentColor"/></IconButton><button className="main-play" onClick={togglePlay}>{playing ? <Pause size={23} fill="currentColor"/> : <Play size={23} fill="currentColor"/>}</button><IconButton onClick={() => nextTrack()}><SkipForward size={20} fill="currentColor"/></IconButton><IconButton active={settings.repeat !== 'off'} onClick={cycleRepeat}>{settings.repeat === 'one' ? <Repeat1 size={18}/> : <Repeat size={18}/>}</IconButton></div><div className="progress-row"><span>{fmt(position)}</span><input type="range" min="0" max={Math.max(1, duration)} step="0.1" value={Math.min(position, duration || 0)} onChange={e => { const v = Number(e.target.value); if (audioRef.current) audioRef.current.currentTime = v; setPosition(v) }}/><span>{fmt(duration)}</span></div></div>
      <div className="player-right"><IconButton active={queueOpen} onClick={() => setQueueOpen(v => !v)}><ListMusic size={19}/></IconButton><IconButton onClick={() => setSettingsOpen(true)}><SlidersHorizontal size={19}/></IconButton>{settings.volume === 0 ? <VolumeX size={18}/> : settings.volume < .5 ? <Volume1 size={18}/> : <Volume2 size={18}/>}<input className="volume" type="range" min="0" max="1" step="0.01" value={settings.volume} onChange={e => saveSettings({ volume: Number(e.target.value) })}/></div>
    </footer>

    {settingsOpen && <Modal onClose={() => setSettingsOpen(false)}><div className="modal-head"><div><p className="eyebrow">NEO PLAYER</p><h2>Settings</h2></div><IconButton onClick={() => setSettingsOpen(false)}><X size={22}/></IconButton></div><div className="setting-group"><h3>Orange theme</h3><p>Dark interface with orange-only accent presets.</p><div className="theme-grid">{ORANGE_THEMES.map(t => <button key={t.id} className={`theme-choice ${settings.theme === t.id ? 'selected' : ''}`} onClick={() => saveSettings({ theme: t.id })}><span style={{ background: t.color }}/><b>{t.name}</b></button>)}</div></div><div className="setting-group"><h3>Playback</h3><label><span>Speed <b>{Number(settings.playbackRate || 1).toFixed(2)}×</b></span><input type="range" min="0.5" max="2" step="0.05" value={settings.playbackRate} onChange={e => saveSettings({ playbackRate: Number(e.target.value) })}/></label><label><span>Default volume <b>{Math.round(settings.volume * 100)}%</b></span><input type="range" min="0" max="1" step="0.01" value={settings.volume} onChange={e => saveSettings({ volume: Number(e.target.value) })}/></label></div><div className="setting-group"><div className="section-heading"><div><h3>Music folders</h3><p>Everything stays on this laptop.</p></div><button className="pill secondary" onClick={chooseFolder}><Plus size={16}/> Add</button></div><div className="folder-list">{folders.map(path => <div key={path}><span className="ellipsis">{path}</span><IconButton onClick={async () => { await api.removeFolder(path); setFolders(await api.folders()) }}><Trash2 size={17}/></IconButton></div>)}</div></div></Modal>}

    {playlistPickerSong && <Modal small onClose={() => setPlaylistPickerSong(null)}><div className="modal-head"><div><p className="eyebrow">ADD TO PLAYLIST</p><h2>{playlistPickerSong.title}</h2></div><IconButton onClick={() => setPlaylistPickerSong(null)}><X size={22}/></IconButton></div><div className="playlist-picker"><button className="picker-create" onClick={async () => { const name = window.prompt('Playlist name', 'New playlist'); if (!name?.trim()) return; const p = await api.createPlaylist(name.trim()); setPlaylists(await api.playlists()); await api.addPlaylistSong(p.id, playlistPickerSong.id); setPlaylists(await api.playlists()); setPlaylistPickerSong(null); showToast(`Added to ${p.name}`) }}><Plus size={18}/> Create new playlist</button>{playlists.map(p => <button key={p.id} onClick={() => addSongToPlaylist(p)}><div className="side-cover"><ListMusic size={18}/></div><div><b>{p.name}</b><span>{p.count} songs</span></div></button>)}</div></Modal>}

    {toast && <div className="toast">{toast}</div>}
  </div>
}

function CollectionHero({ song, icon, round = false, type, title, subtitle, onPlay, actions }) {
  return <div className="playlist-hero">
    {song ? <Cover song={song} size={180} className={`playlist-art-cover ${round ? 'artist-cover' : ''}`}/> : <div className={`playlist-art ${round ? 'artist-cover' : ''}`}>{icon}</div>}
    <div><p>{type}</p><h1>{title}</h1><span>{subtitle}</span><div className="playlist-controls"><button className="round-play" onClick={onPlay}><Play size={26} fill="currentColor"/></button>{actions}</div></div>
  </div>
}

function Modal({ children, onClose, small = false }) {
  return <div className="modal-backdrop" onMouseDown={e => e.target === e.currentTarget && onClose()}><div className={`settings-modal ${small ? 'small-modal' : ''}`}>{children}</div></div>
}

function EmptyLibrary({ onAdd }) {
  return <div className="empty-library"><div className="empty-icon"><Music2 size={42}/></div><h3>Your library is empty</h3><p>Add a music folder from this PC. NEO indexes files locally.</p><button className="primary-cta" onClick={onAdd}><FolderPlus size={18}/> Choose folder</button></div>
}
