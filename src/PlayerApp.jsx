import React, { useEffect, useMemo, useRef, useState } from 'react'
import {
  Album, AudioLines, BarChart3, ChevronDown, ChevronLeft, ChevronRight, Clock3, Disc3, Download,
  Folder, FolderPlus, Gauge, Globe2, Heart, Home, Library, ListMusic, Menu, Mic2, Monitor, Moon,
  MoreHorizontal, Music2, Pause, Pin, Play, Plus, Radio, Repeat, Repeat1, ScanSearch, Search,
  Settings, Shuffle, SkipBack, SkipForward, SlidersHorizontal, Sparkles, Sun, Trash2, Upload,
  UserRound, Volume1, Volume2, VolumeX, Wand2, X, EyeOff, Languages, Palette, TimerReset
} from 'lucide-react'
import { api } from './api'
import { ACCENTS, FONT_OPTIONS, applyAppearance } from './theme'
import { translator } from './i18n'

const fmt = (seconds) => {
  if (!Number.isFinite(seconds) || seconds < 0) return '0:00'
  const h = Math.floor(seconds / 3600)
  const min = Math.floor((seconds % 3600) / 60)
  const sec = Math.floor(seconds % 60).toString().padStart(2, '0')
  return h ? `${h}:${String(min).padStart(2,'0')}:${sec}` : `${min}:${sec}`
}
const bytes = (value) => {
  const n = Number(value || 0)
  if (n < 1024 ** 2) return `${Math.round(n / 1024)} KB`
  if (n < 1024 ** 3) return `${(n / 1024 ** 2).toFixed(1)} MB`
  return `${(n / 1024 ** 3).toFixed(1)} GB`
}
const compareText = (a, b) => String(a || '').localeCompare(String(b || ''), undefined, { sensitivity:'base', numeric:true })
const unique = (items) => [...new Set(items.filter(Boolean))]

function NeoLogo({ size=38 }) {
  return <img className="neo-logo" src="/neo-mark.svg" width={size} height={size} alt="NEO PLAYER" draggable="false" />
}
function IconButton({ children, active=false, title='', onClick, className='' }) {
  return <button className={`icon-button ${active ? 'active' : ''} ${className}`} title={title} onClick={onClick}>{children}</button>
}
function Toggle({ checked, onChange, label, hint }) {
  return <label className="setting-row toggle-row"><span><b>{label}</b>{hint && <small>{hint}</small>}</span><input type="checkbox" checked={!!checked} onChange={e=>onChange(e.target.checked)}/><i/></label>
}
function Cover({ song, size=48, className='' }) {
  const [failed, setFailed] = useState(false)
  useEffect(()=>setFailed(false), [song?.id])
  return <div className={`cover ${className}`} style={{width:size,height:size}}>
    {!failed && song?.cover ? <img src={song.cover} alt="" onError={()=>setFailed(true)}/> : <Music2 size={Math.max(18,size*.34)}/>} 
  </div>
}
function SectionTitle({ title, subtitle, action }) {
  return <div className="section-title"><div><h2>{title}</h2>{subtitle && <p>{subtitle}</p>}</div>{action}</div>
}
function EmptyState({ icon=<Music2/>, title, hint, action }) {
  return <div className="empty-state"><div className="empty-icon">{icon}</div><h2>{title}</h2><p>{hint}</p>{action}</div>
}

function Visualizer({ analyserRef, mode='bars', playing, sensitivity=1 }) {
  const canvasRef = useRef(null)
  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas) return
    let raf = 0
    const draw = () => {
      const ctx = canvas.getContext('2d')
      const w = canvas.clientWidth || 700, h = canvas.clientHeight || 180
      const dpr = Math.max(1, window.devicePixelRatio || 1)
      if (canvas.width !== Math.floor(w*dpr) || canvas.height !== Math.floor(h*dpr)) { canvas.width=w*dpr; canvas.height=h*dpr }
      ctx.setTransform(dpr,0,0,dpr,0,0); ctx.clearRect(0,0,w,h)
      const analyser = analyserRef.current
      const accent = getComputedStyle(document.documentElement).getPropertyValue('--accent').trim() || '#ff7a1a'
      if (!analyser || !playing || mode === 'off') { raf=requestAnimationFrame(draw); return }
      if (mode === 'waveform') {
        const data = new Uint8Array(analyser.fftSize); analyser.getByteTimeDomainData(data)
        ctx.beginPath(); ctx.lineWidth=2; ctx.strokeStyle=accent
        data.forEach((v,i)=>{ const x=i/(data.length-1)*w; const y=h/2+(v-128)/128*h*.38*sensitivity; i?ctx.lineTo(x,y):ctx.moveTo(x,y) })
        ctx.stroke()
      } else {
        const data = new Uint8Array(analyser.frequencyBinCount); analyser.getByteFrequencyData(data)
        const count = mode === 'pulse' ? 36 : 72; const gap=3; const bw=(w-gap*(count-1))/count
        for(let i=0;i<count;i++){ const index=Math.floor(i*data.length/count); const value=Math.min(1,data[index]/220*sensitivity); const bh=Math.max(3,value*h*.92); ctx.globalAlpha=.35+value*.65; ctx.fillStyle=accent; ctx.fillRect(i*(bw+gap),h-bh,bw,bh) }
        ctx.globalAlpha=1
      }
      raf=requestAnimationFrame(draw)
    }
    raf=requestAnimationFrame(draw)
    return ()=>cancelAnimationFrame(raf)
  }, [analyserRef, mode, playing, sensitivity])
  return <canvas className="visualizer" ref={canvasRef}/>
}

function TrackRow({ song, index, current, playing, onPlay, onFavorite, onQueue, onPlaylist, onHide, extra }) {
  return <div className={`track-row ${current ? 'current' : ''}`} onDoubleClick={()=>onPlay(song)}>
    <div className="track-index"><span>{index+1}</span><button onClick={()=>onPlay(song)}>{current&&playing?<Pause size={15}/>:<Play size={15} fill="currentColor"/>}</button></div>
    <div className="track-main"><Cover song={song} size={44}/><div className="ellipsis"><b>{song.title}</b><span>{song.artist || '—'}</span></div></div>
    <div className="track-meta ellipsis"><span>{song.album || '—'}</span><small>{[song.year||'',song.genre||''].filter(Boolean).join(' · ')}</small></div>
    <div className="track-actions">
      <IconButton active={song.favorite} onClick={()=>onFavorite(song)}><Heart size={16} fill={song.favorite?'currentColor':'none'}/></IconButton>
      <IconButton onClick={()=>onQueue(song)}><ListMusic size={16}/></IconButton>
      <IconButton onClick={()=>onPlaylist(song)}><Plus size={16}/></IconButton>
      <IconButton onClick={()=>onHide(song)}><EyeOff size={16}/></IconButton>{extra}
    </div>
    <div className="track-duration">{fmt(song.duration)}</div>
  </div>
}

function EntityCard({ title, subtitle, song, icon=<Disc3/>, onClick, round=false, badge }) {
  return <button className="entity-card" onClick={onClick}>
    <div className={`entity-art ${round?'round':''}`}>{song?<Cover song={song} size={180}/>:icon}<span className="card-play"><Play size={22} fill="currentColor"/></span>{badge&&<em>{badge}</em>}</div>
    <b>{title}</b><span>{subtitle}</span>
  </button>
}

function parseLrc(text='') {
  const out=[]
  String(text).split(/\r?\n/).forEach(line=>{
    const matches=[...line.matchAll(/\[(\d{1,2}):(\d{2})(?:[.:](\d{1,3}))?\]/g)]
    if(!matches.length)return
    const content=line.replace(/\[[^\]]+\]/g,'').trim()
    matches.forEach(m=>out.push({time:Number(m[1])*60+Number(m[2])+(Number(m[3]||0)/10**String(m[3]||'').length),text:content}))
  })
  return out.sort((a,b)=>a.time-b.time)
}

function LyricsPanel({ data, position, settings, t, onEdit }) {
  const lines=useMemo(()=>parseLrc(data?.lrc||''),[data?.lrc])
  const active=Math.max(0,lines.findLastIndex?.(x=>x.time<=position) ?? lines.reduce((a,x,i)=>x.time<=position?i:a,0))
  if(!data) return <div className="lyrics-panel skeleton"/>
  return <section className="lyrics-panel"><div className="panel-head"><b>{t('lyrics')}</b><button className="text-button" onClick={onEdit}>{t('editLyrics')}</button></div>
    {lines.length ? <div className="synced-lyrics" style={{fontSize:settings.lyricsFontSize||20}}>{lines.map((line,i)=><p key={`${line.time}-${i}`} className={i===active?'active':''}>{line.text||'♪'}</p>)}</div>
      : <div className="plain-lyrics" style={{fontSize:settings.lyricsFontSize||20,whiteSpace:'pre-wrap'}}>{data.plain || '—'}</div>}
    {settings.translationEnabled && data.translation && <div className="translation-layer">{data.translation}</div>}
    {settings.romanizationEnabled && data.romanization && <div className="romanization-layer">{data.romanization}</div>}
  </section>
}

function Modal({ title, onClose, children, wide=false }) {
  return <div className="modal-backdrop" onMouseDown={e=>e.target===e.currentTarget&&onClose()}><div className={`modal ${wide?'wide':''}`}><div className="modal-head"><h2>{title}</h2><IconButton onClick={onClose}><X/></IconButton></div>{children}</div></div>
}

export default function PlayerApp() {
  const deckA=useRef(null), deckB=useRef(null), activeDeck=useRef('a'), audioContext=useRef(null), gains=useRef({}), analyserRef=useRef(null), filtersRef=useRef([]), crossfadeLock=useRef(false)
  const [page,setPage]=useState('home'), [libraryMode,setLibraryMode]=useState('tracks'), [library,setLibrary]=useState([]), [favorites,setFavorites]=useState([])
  const [history,setHistory]=useState([]), [folders,setFolders]=useState([]), [playlists,setPlaylists]=useState([]), [playlistFolders,setPlaylistFolders]=useState([]), [stats,setStats]=useState({})
  const [selectedPlaylist,setSelectedPlaylist]=useState(null), [selectedAlbum,setSelectedAlbum]=useState(null), [selectedArtist,setSelectedArtist]=useState(null), [selectedGenre,setSelectedGenre]=useState(null)
  const [queue,setQueue]=useState([]), [current,setCurrent]=useState(null), [playing,setPlaying]=useState(false), [position,setPosition]=useState(0), [duration,setDuration]=useState(0)
  const [query,setQuery]=useState(''), [sort,setSort]=useState('artist'), [settings,setSettings]=useState({}), [profile,setProfile]=useState(null), [lyrics,setLyrics]=useState(null)
  const [queueOpen,setQueueOpen]=useState(false), [settingsOpen,setSettingsOpen]=useState(false), [lyricsOpen,setLyricsOpen]=useState(false), [profileOpen,setProfileOpen]=useState(false)
  const [playlistPickerSong,setPlaylistPickerSong]=useState(null), [busy,setBusy]=useState(false), [toast,setToast]=useState(''), [smartMix,setSmartMix]=useState([])
  const [t,lang]=translator(settings.language)

  const showToast=(text)=>{setToast(text);clearTimeout(window.__neoToast);window.__neoToast=setTimeout(()=>setToast(''),2600)}
  const refresh=async()=>{
    const [songs,favs,recent,pls,pfs,dirs,savedQueue,savedSettings,libraryStats]=await Promise.all([
      api.library(),api.favorites(),api.historyList(),api.playlists(),api.playlistFolders(),api.folders(),api.queue(),api.settings(),api.stats()
    ])
    setLibrary(songs);setFavorites(favs);setHistory(recent);setPlaylists(pls);setPlaylistFolders(pfs);setFolders(dirs);setQueue(savedQueue);setSettings(savedSettings);setStats(libraryStats)
  }
  useEffect(()=>{refresh().catch(e=>showToast(e.message))},[])
  useEffect(()=>{applyAppearance(settings,profile,lang)},[settings.themeMode,settings.accent,settings.customColor,settings.fontFamily,settings.fontScale,lang,profile?.accent])

  const ensureGraph=async()=>{
    if(!audioContext.current){
      const Ctx=window.AudioContext||window.webkitAudioContext; if(!Ctx)return
      const ctx=new Ctx();audioContext.current=ctx
      const ga=ctx.createGain(),gb=ctx.createGain();ga.gain.value=1;gb.gain.value=0;gains.current={a:ga,b:gb}
      const freqs=[60,230,910,3600,14000];const fs=freqs.map((f,i)=>{const n=ctx.createBiquadFilter();n.type=i===0?'lowshelf':i===4?'highshelf':'peaking';n.frequency.value=f;n.Q.value=1;return n});filtersRef.current=fs
      const analyser=ctx.createAnalyser();analyser.fftSize=2048;analyser.smoothingTimeConstant=.82;analyserRef.current=analyser
      const sourceA=ctx.createMediaElementSource(deckA.current),sourceB=ctx.createMediaElementSource(deckB.current)
      sourceA.connect(ga);sourceB.connect(gb);ga.connect(fs[0]);gb.connect(fs[0]);for(let i=0;i<fs.length-1;i++)fs[i].connect(fs[i+1]);fs.at(-1).connect(analyser);analyser.connect(ctx.destination)
    }
    if(audioContext.current.state==='suspended')await audioContext.current.resume()
  }
  useEffect(()=>{filtersRef.current.forEach((f,i)=>{f.gain.value=settings.eqEnabled?Number(settings.eqBands?.[i]||0):0}); if(filtersRef.current[0])filtersRef.current[0].gain.value+=(Number(settings.bassBoost||0)/100)*8},[settings.eqEnabled,JSON.stringify(settings.eqBands),settings.bassBoost])

  const getDeck=(name=activeDeck.current)=>name==='a'?deckA.current:deckB.current
  const persistQueue=async(items)=>{setQueue(items);try{await api.setQueue(items.map(x=>x.id))}catch{}}
  const loadDeck=async(name,song,gain=1)=>{
    await ensureGraph();const audio=getDeck(name);audio.src=`/media/${song.id}`;audio.playbackRate=Number(settings.playbackRate||1);audio.volume=Math.max(0,Math.min(1,Number(settings.volume??.82)));audio.currentTime=0
    if(gains.current[name])gains.current[name].gain.setValueAtTime(gain,audioContext.current.currentTime);await audio.play()
  }
  const playTrack=async(song,context=library,crossfade=false)=>{
    if(!song)return;const normalized=context.length?context:[song];persistQueue(normalized)
    const ms=Number(settings.crossfadeMs||0);const oldName=activeDeck.current,newName=oldName==='a'?'b':'a'
    try{
      if(current&&crossfade&&ms>0&&audioContext.current){
        await loadDeck(newName,song,0);const ctx=audioContext.current,now=ctx.currentTime,sec=ms/1000
        gains.current[newName].gain.cancelScheduledValues(now);gains.current[oldName].gain.cancelScheduledValues(now);gains.current[newName].gain.setValueAtTime(0,now);gains.current[oldName].gain.setValueAtTime(1,now);gains.current[newName].gain.linearRampToValueAtTime(1,now+sec);gains.current[oldName].gain.linearRampToValueAtTime(0,now+sec)
        activeDeck.current=newName;setTimeout(()=>{const old=getDeck(oldName);old.pause();old.removeAttribute('src');old.load();crossfadeLock.current=false},ms+80)
      } else {
        getDeck('a').pause();getDeck('b').pause();if(gains.current.a)gains.current.a.gain.value=oldName==='a'?1:0;if(gains.current.b)gains.current.b.gain.value=oldName==='b'?1:0
        await loadDeck(oldName,song,1);activeDeck.current=oldName;crossfadeLock.current=false
      }
      setCurrent(song);setPlaying(true);setPosition(0);setDuration(song.duration||0);api.history(song.id).catch(()=>{})
    }catch(e){setPlaying(false);showToast(e.message)}
  }
  const nextTrack=(fromEnded=false,withCrossfade=true)=>{
    const list=queue.length?queue:library;if(!list.length)return
    if(settings.repeat==='one'&&fromEnded){const a=getDeck();a.currentTime=0;a.play().catch(()=>{});return}
    let idx=Math.max(0,list.findIndex(x=>x.id===current?.id));if(settings.shuffle&&list.length>1){let n=idx;while(n===idx)n=Math.floor(Math.random()*list.length);idx=n}else idx++
    if(idx>=list.length){if(settings.repeat==='all')idx=0;else{setPlaying(false);return}}
    playTrack(list[idx],list,withCrossfade)
  }
  const prevTrack=()=>{const a=getDeck();if(a&&a.currentTime>4){a.currentTime=0;return}const list=queue.length?queue:library;if(!list.length)return;let idx=list.findIndex(x=>x.id===current?.id)-1;if(idx<0)idx=settings.repeat==='all'?list.length-1:0;playTrack(list[idx],list,true)}
  const togglePlay=async()=>{const a=getDeck();if(!current&&library.length)return playTrack(library[0],library);if(!a)return;await ensureGraph();a.paused?a.play().catch(()=>{}):a.pause()}

  useEffect(()=>{
    const handle=(e)=>{const a=e.currentTarget;if(a!==getDeck())return;setPosition(a.currentTime||0);setDuration(a.duration||current?.duration||0);setPlaying(!a.paused)
      const cf=Number(settings.crossfadeMs||0)/1000;if(cf>0&&a.duration&&a.duration-a.currentTime<=cf+.18&&!crossfadeLock.current){crossfadeLock.current=true;nextTrack(true,true)}}
    const ended=(e)=>{if(e.currentTarget===getDeck()&&!crossfadeLock.current)nextTrack(true,false)}
    ;[deckA.current,deckB.current].filter(Boolean).forEach(a=>{a.addEventListener('timeupdate',handle);a.addEventListener('play',handle);a.addEventListener('pause',handle);a.addEventListener('ended',ended)})
    return()=>[deckA.current,deckB.current].filter(Boolean).forEach(a=>{a.removeEventListener('timeupdate',handle);a.removeEventListener('play',handle);a.removeEventListener('pause',handle);a.removeEventListener('ended',ended)})
  },[queue,current,settings.shuffle,settings.repeat,settings.crossfadeMs])
  useEffect(()=>{[deckA.current,deckB.current].filter(Boolean).forEach(a=>{a.volume=Math.max(0,Math.min(1,Number(settings.volume??.82)));a.playbackRate=Number(settings.playbackRate||1)})},[settings.volume,settings.playbackRate])
  useEffect(()=>{if(!current){setLyrics(null);setProfile(null);return}api.lyrics(current.id).then(setLyrics).catch(()=>setLyrics(null));api.profile(current.id).then(setProfile).catch(()=>setProfile(null))},[current?.id])

  useEffect(()=>{const key=e=>{if(['INPUT','TEXTAREA','SELECT'].includes(document.activeElement?.tagName))return;if(e.code==='Space'){e.preventDefault();togglePlay()}if(e.ctrlKey&&e.code==='ArrowRight')nextTrack();if(e.ctrlKey&&e.code==='ArrowLeft')prevTrack()};window.addEventListener('keydown',key);return()=>window.removeEventListener('keydown',key)})

  const albums=useMemo(()=>{const m=new Map();library.forEach(s=>{const k=`${s.album||t('unknownAlbum')}\0${s.artist||t('unknownArtist')}`;if(!m.has(k))m.set(k,{key:k,name:s.album||t('unknownAlbum'),artist:s.artist||t('unknownArtist'),song:s,songs:[]});m.get(k).songs.push(s)});return[...m.values()].sort((a,b)=>compareText(a.name,b.name))},[library,lang])
  const artists=useMemo(()=>{const m=new Map();library.forEach(s=>{const n=s.artist||t('unknownArtist');if(!m.has(n))m.set(n,{name:n,song:s,songs:[]});m.get(n).songs.push(s)});return[...m.values()].sort((a,b)=>compareText(a.name,b.name))},[library,lang])
  const genres=useMemo(()=>unique(library.map(s=>s.genre||t('unknownGenre'))).sort(compareText),[library,lang])
  const sortedLibrary=useMemo(()=>{const x=[...library];if(sort==='title')x.sort((a,b)=>compareText(a.title,b.title));else if(sort==='album')x.sort((a,b)=>compareText(a.album,b.album)||a.track-b.track);else if(sort==='added')x.sort((a,b)=>b.addedAt-a.addedAt);else if(sort==='duration')x.sort((a,b)=>b.duration-a.duration);else x.sort((a,b)=>compareText(a.artist,b.artist)||compareText(a.album,b.album)||a.track-b.track);return x},[library,sort])
  const filtered=useMemo(()=>{const q=query.trim().toLowerCase();if(!q)return sortedLibrary;return sortedLibrary.filter(s=>[s.title,s.artist,s.album,s.genre,s.mood,s.key].some(x=>String(x||'').toLowerCase().includes(q)))},[sortedLibrary,query])

  const saveSettings=async(patch)=>{setSettings(s=>({...s,...patch}));try{setSettings(await api.patchSettings(patch))}catch(e){showToast(e.message)}}
  const toggleFavorite=async(song)=>{const v=!song.favorite;await api.favorite(song.id,v);setLibrary(x=>x.map(s=>s.id===song.id?{...s,favorite:v}:s));setFavorites(await api.favorites());if(current?.id===song.id)setCurrent(c=>({...c,favorite:v}))}
  const hideSong=async(song)=>{await api.hideSong(song.id,true);if(current?.id===song.id){getDeck()?.pause();setCurrent(null)}await refresh();showToast(t('hide'))}
  const addQueue=async(song)=>{const items=[...queue,song];await persistQueue(items);showToast(t('addToQueue'))}
  const removeQueue=async(i)=>persistQueue(queue.filter((_,x)=>x!==i))
  const chooseFolder=async()=>{let path=null;try{path=await window.pywebview?.api?.pick_folder?.()}catch{};if(!path)path=window.prompt('Folder path');if(!path)return;setBusy(true);try{await api.addFolder(path);const r=await api.scan();await refresh();showToast(`${t('scanComplete')} · ${r.updated} · ${r.skippedShort} ${t('skippedShort')}`)}catch(e){showToast(e.message)}finally{setBusy(false)}}
  const scanFolders=async()=>{setBusy(true);try{const r=await api.scan();await refresh();showToast(`${t('scanComplete')} · ${r.updated}`)}catch(e){showToast(e.message)}finally{setBusy(false)}}
  const scanSystem=async()=>{if(!window.confirm(t('systemScanWarning')))return;setBusy(true);try{const r=await api.scanSystem();await refresh();showToast(`${t('scanComplete')} · ${r.updated} · ${r.skippedShort} ${t('skippedShort')}`)}catch(e){showToast(e.message)}finally{setBusy(false)}}
  const createPlaylist=async(folderId=null)=>{const name=window.prompt(t('newPlaylist'));if(!name?.trim())return;const p=await api.createPlaylist(name.trim(),folderId);setPlaylists(await api.playlists());setSelectedPlaylist({...p,songs:[]});setPage('playlist')}
  const createPlaylistFolder=async(parentId=null)=>{const name=window.prompt(t('newFolder'));if(!name?.trim())return;await api.createPlaylistFolder(name.trim(),parentId);setPlaylistFolders(await api.playlistFolders())}
  const openPlaylist=async(id)=>{setSelectedPlaylist(await api.playlist(id));setPage('playlist')}
  const addSongToPlaylist=async(p)=>{if(!playlistPickerSong)return;await api.addPlaylistSong(p.id,playlistPickerSong.id);if(selectedPlaylist?.id===p.id)setSelectedPlaylist(await api.playlist(p.id));setPlaylistPickerSong(null);showToast(t('success'))}
  const smart=async(seed=current)=>{const items=await api.smartMix(seed?.id||null,{limit:60});setSmartMix(items);if(items.length){setQueue(items);await api.setQueue(items.map(x=>x.id));setPage('smart')}}
  const saveLyrics=async(data)=>{const saved=await api.saveLyrics(current.id,data);setLyrics(saved);setLyricsOpen(false);showToast(t('success'))}
  const saveProfile=async(data)=>{const saved=await api.saveProfile(current.id,data);setProfile(saved);setProfileOpen(false);showToast(t('success'))}
  const exportBackup=async()=>{const data=await api.backup();const blob=new Blob([JSON.stringify(data,null,2)],{type:'application/json'});const a=document.createElement('a');a.href=URL.createObjectURL(blob);a.download=`NEO-Backup-${new Date().toISOString().slice(0,10)}.json`;a.click();URL.revokeObjectURL(a.href)}
  const importBackup=(file)=>{if(!file)return;const r=new FileReader();r.onload=async()=>{try{await api.restore(JSON.parse(r.result));await refresh();showToast(t('success'))}catch(e){showToast(e.message)}};r.readAsText(file)}

  const renderTracks=(songs,extraBuilder)=> <div className="track-table"><div className="track-head"><span>#</span><span>{t('title')}</span><span>{t('album')}</span><span></span><Clock3 size={15}/></div>{songs.map((song,i)=><TrackRow key={`${song.id}-${i}`} song={song} index={i} current={current?.id===song.id} playing={playing} onPlay={s=>playTrack(s,songs)} onFavorite={toggleFavorite} onQueue={addQueue} onPlaylist={setPlaylistPickerSong} onHide={hideSong} extra={extraBuilder?.(song)}/>)}</div>

  const sidebarItem=(id,Icon,label)=><button className={`nav-item ${page===id?'selected':''}`} onClick={()=>setPage(id)}><Icon size={20}/><span>{t(label)}</span></button>

  const libraryContent=()=>{
    if(libraryMode==='albums')return <div className="entity-grid">{albums.map(a=><EntityCard key={a.key} title={a.name} subtitle={`${a.artist} · ${a.songs.length}`} song={a.song} onClick={()=>{setSelectedAlbum(a);setPage('album')}}/>)}</div>
    if(libraryMode==='artists')return <div className="entity-grid">{artists.map(a=><EntityCard key={a.name} title={a.name} subtitle={`${a.songs.length} ${t('songs')}`} song={a.song} round onClick={()=>{setSelectedArtist(a);setPage('artist')}}/>)}</div>
    if(libraryMode==='genres')return <div className="genre-grid">{genres.map((g,i)=><button key={g} className={`genre-card g${i%8}`} onClick={()=>{setSelectedGenre(g);setPage('genre')}}><Disc3/><b>{g}</b><span>{library.filter(s=>(s.genre||t('unknownGenre'))===g).length} {t('tracks')}</span></button>)}</div>
    if(libraryMode==='folders')return <div className="folder-list">{folders.map(f=><div className="folder-row" key={f}><Folder/><span>{f}</span><IconButton onClick={()=>api.removeFolder(f).then(refresh)}><Trash2 size={16}/></IconButton></div>)}</div>
    return renderTracks(filtered)
  }

  const pageContent=()=>{
    if(!library.length && !['settings','create'].includes(page))return <EmptyState icon={<ScanSearch size={34}/>} title={t('noMusic')} hint={t('noMusicHint')} action={<div className="empty-actions"><button className="primary" onClick={chooseFolder}>{t('addFolder')}</button><button onClick={scanSystem}>{t('scanSystem')}</button></div>}/>
    if(page==='home')return <><section className="hero"><div><span className="eyebrow">NEO PLAYER · LOCAL FIRST</span><h1>{lang==='fa'?'موسیقی خودت، هوشمندتر و کاملاً محلی':'Your music, smarter and fully local'}</h1><p>{lang==='fa'?'پخش آفلاین، میکس هوشمند، متن هماهنگ، تم‌های کامل و کنترل عمیق روی کتابخانه.':'Offline playback, smart mixes, synced lyrics, rich themes and deep control over your library.'}</p><div className="hero-actions"><button className="primary" onClick={()=>smart()}><Sparkles size={18}/>{t('smartMix')}</button><button onClick={chooseFolder}><FolderPlus size={18}/>{t('addFolder')}</button></div></div><NeoLogo size={170}/></section>
      <SectionTitle title={t('recent')} subtitle={t('fromLibrary')}/><div className="entity-grid compact">{history.slice(0,8).map(s=><EntityCard key={s.id} title={s.title} subtitle={s.artist||t('unknownArtist')} song={s} onClick={()=>playTrack(s,history)}/>)}</div>
      <SectionTitle title={t('liked')} /><div className="quick-list">{favorites.slice(0,6).map(s=><button key={s.id} onClick={()=>playTrack(s,favorites)}><Cover song={s} size={54}/><span><b>{s.title}</b><small>{s.artist}</small></span><Play/></button>)}</div></>
    if(page==='search')return <><div className="search-hero"><Search/><input autoFocus value={query} onChange={e=>setQuery(e.target.value)} placeholder={t('searchPlaceholder')}/>{query&&<IconButton onClick={()=>setQuery('')}><X/></IconButton>}</div><SectionTitle title={query?t('search'):t('library')} subtitle={`${filtered.length} ${t('tracks')}`}/>{renderTracks(filtered)}</>
    if(page==='library')return <><SectionTitle title={t('library')} action={<div className="toolbar"><select value={sort} onChange={e=>setSort(e.target.value)}><option value="artist">{t('artist')}</option><option value="title">{t('title')}</option><option value="album">{t('album')}</option><option value="added">{t('dateAdded')}</option><option value="duration">{t('duration')}</option></select><button onClick={chooseFolder}><FolderPlus size={17}/>{t('addFolder')}</button><button onClick={scanFolders}><ScanSearch size={17}/>{t('scanFolders')}</button></div>}/><div className="tabs">{[['tracks','songs'],['albums','albums'],['artists','artists'],['genres','genres'],['folders','folders']].map(([id,label])=><button className={libraryMode===id?'active':''} onClick={()=>setLibraryMode(id)} key={id}>{t(label)}</button>)}</div>{libraryContent()}</>
    if(page==='liked')return <><SectionTitle title={t('liked')} subtitle={`${favorites.length} ${t('tracks')}`}/>{renderTracks(favorites)}</>
    if(page==='recent')return <><SectionTitle title={t('recent')} subtitle={`${history.length} ${t('tracks')}`}/>{renderTracks(history)}</>
    if(page==='smart')return <><SectionTitle title={t('smartMix')} subtitle={t('recommendations')} action={<button onClick={()=>smart(current)}><Wand2 size={17}/>{t('smartMix')}</button>}/>{renderTracks(smartMix)}</>
    if(page==='album'&&selectedAlbum)return <><div className="entity-header"><Cover song={selectedAlbum.song} size={190}/><div><span>{t('album')}</span><h1>{selectedAlbum.name}</h1><p>{selectedAlbum.artist} · {selectedAlbum.songs.length} {t('tracks')} · {fmt(selectedAlbum.songs.reduce((a,s)=>a+s.duration,0))}</p><button className="round-play" onClick={()=>playTrack(selectedAlbum.songs[0],selectedAlbum.songs)}><Play fill="currentColor"/></button></div></div>{renderTracks(selectedAlbum.songs)}</>
    if(page==='artist'&&selectedArtist)return <><div className="entity-header artist"><Cover song={selectedArtist.song} size={190}/><div><span>{t('artist')}</span><h1>{selectedArtist.name}</h1><p>{selectedArtist.songs.length} {t('tracks')}</p><button className="round-play" onClick={()=>playTrack(selectedArtist.songs[0],selectedArtist.songs)}><Play fill="currentColor"/></button></div></div>{renderTracks(selectedArtist.songs)}</>
    if(page==='genre'&&selectedGenre){const songs=library.filter(s=>(s.genre||t('unknownGenre'))===selectedGenre);return <><SectionTitle title={selectedGenre} subtitle={`${songs.length} ${t('tracks')}`}/>{renderTracks(songs)}</>}
    if(page==='playlists')return <><SectionTitle title={t('playlists')} action={<div className="toolbar"><button onClick={()=>createPlaylist()}><Plus/>{t('newPlaylist')}</button><button onClick={()=>createPlaylistFolder()}><FolderPlus/>{t('newFolder')}</button></div>}/><div className="playlist-layout"><div className="folder-tree"><b>{t('playlistFolder')}</b>{playlistFolders.filter(f=>!f.hidden).map(f=><div className="playlist-folder" key={f.id} style={{paddingInlineStart:f.parent_id?24:8}}><Folder size={17}/><span>{f.name}</span><button onClick={()=>createPlaylist(f.id)}><Plus size={14}/></button><button onClick={()=>createPlaylistFolder(f.id)}><FolderPlus size={14}/></button></div>)}</div><div className="entity-grid">{playlists.map(p=><EntityCard key={p.id} title={p.name} subtitle={`${p.count} ${t('tracks')}`} icon={<ListMusic size={54}/>} badge={p.pinned?'PIN':''} onClick={()=>openPlaylist(p.id)}/>)}</div></div></>
    if(page==='playlist'&&selectedPlaylist)return <><div className="entity-header playlist"><div className="playlist-big-icon"><ListMusic size={70}/></div><div><span>{t('playlists')}</span><h1>{selectedPlaylist.name}</h1><p>{selectedPlaylist.songs.length} {t('tracks')} · {fmt(selectedPlaylist.songs.reduce((a,s)=>a+s.duration,0))}</p><div className="header-actions"><button className="round-play" onClick={()=>playTrack(selectedPlaylist.songs[0],selectedPlaylist.songs)}><Play fill="currentColor"/></button><IconButton active={selectedPlaylist.pinned} onClick={async()=>{await api.patchPlaylist(selectedPlaylist.id,{pinned:!selectedPlaylist.pinned});setSelectedPlaylist(await api.playlist(selectedPlaylist.id));setPlaylists(await api.playlists())}}><Pin/></IconButton><IconButton onClick={async()=>{await api.deletePlaylist(selectedPlaylist.id);setPage('playlists');await refresh()}}><Trash2/></IconButton></div></div></div>{renderTracks(selectedPlaylist.songs,s=><IconButton onClick={async()=>{await api.removePlaylistSong(selectedPlaylist.id,s.id);setSelectedPlaylist(await api.playlist(selectedPlaylist.id))}}><Trash2 size={16}/></IconButton>)}</>
    if(page==='now'&&current)return <div className="now-page"><div className="now-visual"><Cover song={current} size={360}/><Visualizer analyserRef={analyserRef} mode={profile?.visualizer||settings.visualizer||'bars'} playing={playing} sensitivity={settings.visualizerSensitivity||1}/></div><div className="now-info"><span>{t('nowPlaying')}</span><h1>{current.title}</h1><h3>{current.artist||t('unknownArtist')}</h3><p>{current.album||t('unknownAlbum')} {current.year?`· ${current.year}`:''}</p><div className="now-actions"><button onClick={()=>setLyricsOpen(true)}><Mic2/>{t('lyrics')}</button><button onClick={()=>setProfileOpen(true)}><Palette/>{t('trackAppearance')}</button><button onClick={()=>smart(current)}><Radio/>{t('smartMix')}</button></div><LyricsPanel data={lyrics} position={position} settings={settings} t={t} onEdit={()=>setLyricsOpen(true)}/></div></div>
    if(page==='create')return <div className="create-grid"><button onClick={()=>createPlaylist()}><ListMusic/><b>{t('newPlaylist')}</b><span>{t('fromLibrary')}</span></button><button onClick={()=>createPlaylistFolder()}><FolderPlus/><b>{t('newFolder')}</b><span>{t('nestedFolders')}</span></button><button onClick={()=>smart()}><Sparkles/><b>{t('smartMix')}</b><span>{t('recommendations')}</span></button></div>
    if(page==='settings')return <SettingsPage t={t} lang={lang} settings={settings} saveSettings={saveSettings} folders={folders} chooseFolder={chooseFolder} scanFolders={scanFolders} scanSystem={scanSystem} busy={busy} stats={stats} exportBackup={exportBackup} importBackup={importBackup}/>
    return null
  }

  return <div className={`app-shell ${settings.reduceMotion?'reduce-motion':''}`}>
    <audio ref={deckA} preload="metadata"/><audio ref={deckB} preload="metadata"/>
    <aside className="sidebar"><div className="brand"><NeoLogo/><div><b>NEO PLAYER</b><small>Desktop · Offline Pro</small></div></div><nav>{sidebarItem('home',Home,'home')}{sidebarItem('search',Search,'search')}{sidebarItem('library',Library,'library')}{sidebarItem('liked',Heart,'liked')}{sidebarItem('recent',Clock3,'recent')}{sidebarItem('playlists',ListMusic,'playlists')}{sidebarItem('smart',Sparkles,'smartMix')}{sidebarItem('create',Plus,'create')}</nav><div className="sidebar-bottom">{sidebarItem('settings',Settings,'settings')}<div className="mini-stats"><b>{stats.songs||0}</b><span>{t('tracks')}</span><b>{bytes(stats.bytes||0)}</b><span>{t('storage')}</span></div></div></aside>
    <main className="content"><header className="topbar"><div className="history-buttons"><IconButton onClick={()=>history.back?.()}><ChevronLeft/></IconButton><IconButton><ChevronRight/></IconButton></div><div className="top-actions"><button className="pill" onClick={()=>setPage('search')}><Search size={17}/>{t('search')}</button><IconButton onClick={()=>setSettingsOpen(true)}><SlidersHorizontal/></IconButton></div></header><div className="page">{pageContent()}</div></main>
    {queueOpen&&<aside className="right-panel"><div className="panel-head"><h3>{t('queue')}</h3><div><button className="text-button" onClick={()=>persistQueue([])}>{t('clear')}</button><IconButton onClick={()=>setQueueOpen(false)}><X/></IconButton></div></div>{queue.map((s,i)=><div className={`queue-item ${s.id===current?.id?'current':''}`} key={`${s.id}-${i}`}><Cover song={s} size={42}/><button onClick={()=>playTrack(s,queue)}><b>{s.title}</b><small>{s.artist}</small></button><IconButton onClick={()=>removeQueue(i)}><X size={15}/></IconButton></div>)}</aside>}
    <footer className="playerbar"><div className="player-song">{current?<><Cover song={current} size={58}/><button onClick={()=>setPage('now')}><b>{current.title}</b><span>{current.artist||t('unknownArtist')}</span></button><IconButton active={current.favorite} onClick={()=>toggleFavorite(current)}><Heart size={18} fill={current.favorite?'currentColor':'none'}/></IconButton></>:<><div className="empty-cover"><Music2/></div><span>NEO PLAYER</span></>}</div><div className="transport"><div className="transport-buttons"><IconButton active={settings.shuffle} onClick={()=>saveSettings({shuffle:!settings.shuffle})}><Shuffle size={17}/></IconButton><IconButton onClick={prevTrack}><SkipBack fill="currentColor"/></IconButton><button className="main-play" onClick={togglePlay}>{playing?<Pause fill="currentColor"/>:<Play fill="currentColor"/>}</button><IconButton onClick={()=>nextTrack(false,true)}><SkipForward fill="currentColor"/></IconButton><IconButton active={settings.repeat!=='off'} onClick={()=>saveSettings({repeat:settings.repeat==='off'?'all':settings.repeat==='all'?'one':'off'})}>{settings.repeat==='one'?<Repeat1/>:<Repeat/>}</IconButton></div><div className="progress-row"><span>{fmt(position)}</span><input type="range" min="0" max={duration||1} step=".1" value={Math.min(position,duration||1)} onChange={e=>{const a=getDeck();if(a)a.currentTime=Number(e.target.value)}}/><span>{fmt(duration)}</span></div></div><div className="player-tools"><select value={settings.playbackRate||1} onChange={e=>saveSettings({playbackRate:Number(e.target.value)})}><option>.75×</option><option>1×</option><option>1.25×</option><option>1.5×</option><option>2×</option></select><IconButton onClick={()=>setQueueOpen(!queueOpen)} active={queueOpen}><ListMusic/></IconButton>{Number(settings.volume||0)===0?<VolumeX/>:Number(settings.volume)<.5?<Volume1/>:<Volume2/>}<input className="volume" type="range" min="0" max="1" step=".01" value={settings.volume??.82} onChange={e=>saveSettings({volume:Number(e.target.value)})}/></div></footer>
    {settingsOpen&&<Modal title={t('settings')} onClose={()=>setSettingsOpen(false)} wide><SettingsPage compact t={t} lang={lang} settings={settings} saveSettings={saveSettings} folders={folders} chooseFolder={chooseFolder} scanFolders={scanFolders} scanSystem={scanSystem} busy={busy} stats={stats} exportBackup={exportBackup} importBackup={importBackup}/></Modal>}
    {playlistPickerSong&&<Modal title={t('addToPlaylist')} onClose={()=>setPlaylistPickerSong(null)}><div className="picker-list"><button onClick={()=>createPlaylist()}><Plus/>{t('newPlaylist')}</button>{playlists.map(p=><button key={p.id} onClick={()=>addSongToPlaylist(p)}><ListMusic/><span><b>{p.name}</b><small>{p.count} {t('tracks')}</small></span></button>)}</div></Modal>}
    {lyricsOpen&&current&&<LyricsEditor t={t} data={lyrics||{}} onClose={()=>setLyricsOpen(false)} onSave={saveLyrics}/>} 
    {profileOpen&&current&&<ProfileEditor t={t} profile={profile||{}} onClose={()=>setProfileOpen(false)} onSave={saveProfile}/>} 
    {toast&&<div className="toast">{toast}</div>}{busy&&<div className="busy-layer"><div className="spinner"/><span>{t('scanning')}</span></div>}
  </div>
}

function SettingsPage({t,lang,settings,saveSettings,folders,chooseFolder,scanFolders,scanSystem,busy,stats,exportBackup,importBackup,compact=false}){
  const fileRef=useRef(null)
  const eq=settings.eqBands||[0,0,0,0,0]
  const setEq=(i,v)=>{const next=[...eq];next[i]=Number(v);saveSettings({eqBands:next})}
  return <div className={`settings-page ${compact?'compact':''}`}>
    <SectionTitle title={t('appearance')} subtitle={lang==='fa'?'تم، رنگ، فونت و زبان رابط':'Theme, color, typography and language'}/>
    <div className="settings-grid"><div className="setting-card span2"><label>{t('themeMode')}</label><div className="segmented">{[['system',Monitor],['dark',Moon],['light',Sun],['amoled',Disc3]].map(([id,Icon])=><button key={id} className={settings.themeMode===id?'active':''} onClick={()=>saveSettings({themeMode:id})}><Icon size={17}/>{t(id)}</button>)}</div></div>
      <div className="setting-card span2"><label>{t('accent')}</label><div className="swatches">{ACCENTS.map(a=><button key={a.id} className={settings.accent===a.id?'selected':''} style={{'--swatch':a.color}} onClick={()=>saveSettings({accent:a.id})} title={t(a.label)}/>) }<label className={`custom-swatch ${settings.accent==='custom'?'selected':''}`}><input type="color" value={settings.customColor||'#ff7a1a'} onChange={e=>saveSettings({accent:'custom',customColor:e.target.value})}/></label></div></div>
      <div className="setting-card"><label>{t('font')}</label><select value={settings.fontFamily||'vazirmatn'} onChange={e=>saveSettings({fontFamily:e.target.value})}>{FONT_OPTIONS.map(f=><option key={f.id} value={f.id}>{f.name}</option>)}</select></div>
      <div className="setting-card"><label>{t('fontSize')} · {Math.round((settings.fontScale||1)*100)}%</label><input type="range" min=".8" max="1.4" step=".05" value={settings.fontScale||1} onChange={e=>saveSettings({fontScale:Number(e.target.value)})}/></div>
      <div className="setting-card"><label>{t('language')}</label><select value={settings.language||'system'} onChange={e=>saveSettings({language:e.target.value})}><option value="system">{t('systemLanguage')}</option><option value="fa">{t('persian')}</option><option value="en">{t('english')}</option></select></div>
      <div className="setting-card"><Toggle checked={settings.reduceMotion} onChange={v=>saveSettings({reduceMotion:v})} label={t('reduceMotion')}/></div></div>
    <SectionTitle title={t('library')} subtitle={`${stats.songs||0} ${t('tracks')} · ${bytes(stats.bytes||0)}`}/><div className="settings-grid"><div className="setting-card"><label>{t('minDuration')}</label><div className="inline-input"><input type="number" min="0" max="3600" value={Math.round((settings.minDurationMs||0)/1000)} onChange={e=>saveSettings({minDurationMs:Number(e.target.value)*1000})}/><span>{t('seconds')}</span></div></div><div className="setting-card span2"><div className="button-row"><button onClick={chooseFolder}><FolderPlus/>{t('addFolder')}</button><button onClick={scanFolders} disabled={busy}><ScanSearch/>{t('scanFolders')}</button><button className="danger-outline" onClick={scanSystem} disabled={busy}><Gauge/>{t('scanSystem')}</button></div></div><div className="setting-card span3"><div className="folder-list mini">{folders.length?folders.map(f=><div key={f}><Folder/><span>{f}</span></div>):<small>—</small>}</div></div></div>
    <SectionTitle title={t('playback')}/><div className="settings-grid"><div className="setting-card"><label>{t('crossfade')} · {((settings.crossfadeMs||0)/1000).toFixed(1)}s</label><input type="range" min="0" max="12000" step="500" value={settings.crossfadeMs||0} onChange={e=>saveSettings({crossfadeMs:Number(e.target.value)})}/></div><div className="setting-card"><Toggle checked={settings.gaplessEnabled} onChange={v=>saveSettings({gaplessEnabled:v})} label={t('gapless')}/></div><div className="setting-card"><Toggle checked={settings.automixEnabled} onChange={v=>saveSettings({automixEnabled:v})} label={t('automix')}/></div><div className="setting-card"><Toggle checked={settings.advancedAutomixEnabled} onChange={v=>saveSettings({advancedAutomixEnabled:v})} label={t('advancedAutomix')}/></div><div className="setting-card"><Toggle checked={settings.rememberQueue} onChange={v=>saveSettings({rememberQueue:v})} label={t('rememberQueue')}/></div><div className="setting-card"><Toggle checked={settings.resumeLastSong} onChange={v=>saveSettings({resumeLastSong:v})} label={t('resumeLast')}/></div></div>
    <SectionTitle title={t('audio')}/><div className="settings-grid"><div className="setting-card"><Toggle checked={settings.loudnessNormalization} onChange={v=>saveSettings({loudnessNormalization:v})} label={t('normalize')}/></div><div className="setting-card"><label>{t('targetLufs')} · {settings.normalizationTargetLufs||-14} LUFS</label><input type="range" min="-23" max="-8" step="1" value={settings.normalizationTargetLufs||-14} onChange={e=>saveSettings({normalizationTargetLufs:Number(e.target.value)})}/></div><div className="setting-card span3"><Toggle checked={settings.eqEnabled} onChange={v=>saveSettings({eqEnabled:v})} label={t('eq')}/><div className="eq-grid">{['60','230','910','3.6k','14k'].map((f,i)=><label key={f}><span>{f}</span><input type="range" min="-12" max="12" step="1" value={eq[i]||0} onChange={e=>setEq(i,e.target.value)}/><b>{eq[i]||0}dB</b></label>)}</div></div><div className="setting-card"><label>{t('bass')} · {settings.bassBoost||0}%</label><input type="range" min="0" max="100" value={settings.bassBoost||0} onChange={e=>saveSettings({bassBoost:Number(e.target.value)})}/></div><div className="setting-card"><label>{t('visualizer')}</label><select value={settings.visualizer||'bars'} onChange={e=>saveSettings({visualizer:e.target.value})}><option value="bars">{t('bars')}</option><option value="waveform">{t('waveform')}</option><option value="pulse">{t('pulse')}</option><option value="off">{t('off')}</option></select></div></div>
    <SectionTitle title={t('lyrics')}/><div className="settings-grid"><div className="setting-card"><Toggle checked={settings.translationEnabled} onChange={v=>saveSettings({translationEnabled:v})} label={t('translation')}/></div><div className="setting-card"><Toggle checked={settings.romanizationEnabled} onChange={v=>saveSettings({romanizationEnabled:v})} label={t('romanization')}/></div><div className="setting-card"><Toggle checked={settings.lyricsAutoScroll} onChange={v=>saveSettings({lyricsAutoScroll:v})} label={t('autoScroll')}/></div><div className="setting-card"><label>{t('lyricsSize')} · {settings.lyricsFontSize||20}</label><input type="range" min="12" max="42" value={settings.lyricsFontSize||20} onChange={e=>saveSettings({lyricsFontSize:Number(e.target.value)})}/></div></div>
    <SectionTitle title={t('offline')}/><div className="settings-grid"><div className="setting-card"><Toggle checked={settings.strictOfflineMode} onChange={v=>saveSettings({strictOfflineMode:v})} label={t('strictOffline')}/></div><div className="setting-card"><Toggle checked={settings.offlineBackupEnabled} onChange={v=>saveSettings({offlineBackupEnabled:v})} label={t('offlineBackup')}/></div><div className="setting-card span2"><div className="button-row"><button onClick={exportBackup}><Download/>{t('exportBackup')}</button><button onClick={()=>fileRef.current?.click()}><Upload/>{t('importBackup')}</button><input ref={fileRef} hidden type="file" accept="application/json,.json" onChange={e=>importBackup(e.target.files?.[0])}/></div></div></div>
  </div>
}

function LyricsEditor({t,data,onClose,onSave}){
  const [form,setForm]=useState({plain:data.plain||'',lrc:data.lrc||'',translation:data.translation||'',romanization:data.romanization||''}),[tab,setTab]=useState('lrc')
  const tabs=[['lrc','syncedLyrics'],['plain','plainLyrics'],['translation','translation'],['romanization','romanization']]
  return <Modal title={t('editLyrics')} onClose={onClose} wide><div className="tabs">{tabs.map(([id,label])=><button className={tab===id?'active':''} key={id} onClick={()=>setTab(id)}>{t(label)}</button>)}</div><textarea className="lyrics-editor" value={form[tab]} onChange={e=>setForm({...form,[tab]:e.target.value})} placeholder={tab==='lrc'?'[00:12.40] Lyrics line…':''}/><div className="modal-actions"><button onClick={onClose}>{t('cancel')}</button><button className="primary" onClick={()=>onSave(form)}>{t('save')}</button></div></Modal>
}
function ProfileEditor({t,profile,onClose,onSave}){
  const [form,setForm]=useState({...profile})
  return <Modal title={t('trackAppearance')} onClose={onClose}><div className="profile-form"><label>{t('accent')}<input type="color" value={form.accent||'#ff7a1a'} onChange={e=>setForm({...form,accent:e.target.value})}/></label><label>Background<input type="color" value={form.background||'#101010'} onChange={e=>setForm({...form,background:e.target.value})}/></label><label>{t('visualizer')}<select value={form.visualizer||''} onChange={e=>setForm({...form,visualizer:e.target.value})}><option value="">{t('system')}</option><option value="bars">{t('bars')}</option><option value="waveform">{t('waveform')}</option><option value="pulse">{t('pulse')}</option></select></label><label>Wallpaper opacity<input type="range" min="0" max=".85" step=".05" value={form.wallpaperOpacity??.28} onChange={e=>setForm({...form,wallpaperOpacity:Number(e.target.value)})}/></label></div><div className="modal-actions"><button onClick={onClose}>{t('cancel')}</button><button className="primary" onClick={()=>onSave(form)}>{t('save')}</button></div></Modal>
}
