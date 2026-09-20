import React, { useEffect, useMemo, useState } from 'react'
import { createPortal } from 'react-dom'
import { FolderSearch, Gauge, HardDrive, Maximize2, Minimize2, Music2, Pause, Play, RefreshCw, ScanLine, SkipBack, SkipForward, Sparkles, Volume2, Waves, Zap } from 'lucide-react'
import { api } from './api'

function activeAudio(){const items=[...document.querySelectorAll('audio')];return items.find(a=>a.src?.includes('/media/')&&!a.paused)||items.find(a=>a.src?.includes('/media/'))||null}
function currentSongId(){const match=activeAudio()?.src?.match(/\/media\/(\d+)/);return match?Number(match[1]):null}
function fmt(value){const seconds=Number(value||0);if(!Number.isFinite(seconds)||seconds<0)return'0:00';const m=Math.floor(seconds/60),s=Math.floor(seconds%60).toString().padStart(2,'0');return`${m}:${s}`}
function dispatch(code,ctrlKey=false){window.dispatchEvent(new KeyboardEvent('keydown',{code,ctrlKey,bubbles:true}))}

export default function ImmersiveHub(){
  const [cinema,setCinema]=useState(false)
  const [settingsHost,setSettingsHost]=useState(null)
  const [settings,setSettings]=useState({language:'fa',volume:.82})
  const [library,setLibrary]=useState([])
  const [songId,setSongId]=useState(null)
  const [position,setPosition]=useState(0)
  const [duration,setDuration]=useState(0)
  const [playing,setPlaying]=useState(false)
  const [capabilities,setCapabilities]=useState({nativeCore:false})
  const [scan,setScan]=useState({running:false,phase:'idle',found:0,processed:0,updated:0,unchanged:0,errors:0,elapsedMs:0})
  const [message,setMessage]=useState('')
  const fa=settings.language!=='en'
  const current=useMemo(()=>library.find(x=>x.id===songId)||null,[library,songId])

  const refresh=async()=>{const [saved,songs,caps]=await Promise.all([api.settings(),api.library(),api.nativeCapabilities()]);setSettings(saved);setLibrary(songs);setCapabilities(caps||{nativeCore:false})}
  const toast=text=>{setMessage(text);clearTimeout(window.__neoImmersiveToast);window.__neoImmersiveToast=setTimeout(()=>setMessage(''),3200)}

  useEffect(()=>{refresh().catch(()=>{})},[])
  useEffect(()=>{const find=()=>setSettingsHost(document.querySelector('.settings-page'));find();const observer=new MutationObserver(find);observer.observe(document.body,{childList:true,subtree:true});return()=>observer.disconnect()},[])
  useEffect(()=>{const id=setInterval(()=>{const audio=activeAudio();setSongId(currentSongId());setPosition(audio?.currentTime||0);setDuration(audio?.duration||0);setPlaying(!!audio&&!audio.paused)},250);return()=>clearInterval(id)},[])
  useEffect(()=>{if(!scan.running)return;const id=setInterval(()=>api.scanStatus().then(setScan).catch(()=>{}),300);return()=>clearInterval(id)},[scan.running])
  useEffect(()=>{const key=e=>{if(e.key==='Escape'&&cinema)closeCinema();if(e.key==='F11'){e.preventDefault();cinema?closeCinema():openCinema()}};window.addEventListener('keydown',key);return()=>window.removeEventListener('keydown',key)},[cinema])

  const openCinema=async()=>{setCinema(true);document.documentElement.classList.add('neo-cinema-active');try{if(typeof window.pywebview?.api?.toggle_fullscreen==='function')await window.pywebview.api.toggle_fullscreen();else if(!document.fullscreenElement)await document.documentElement.requestFullscreen?.()}catch{}}
  const closeCinema=async()=>{setCinema(false);document.documentElement.classList.remove('neo-cinema-active');try{if(typeof window.pywebview?.api?.toggle_fullscreen==='function')await window.pywebview.api.toggle_fullscreen();else if(document.fullscreenElement)await document.exitFullscreen?.()}catch{}}
  const seek=value=>{const audio=activeAudio();if(audio)audio.currentTime=Number(value)}
  const setVolume=async value=>{const volume=Math.max(0,Math.min(1,Number(value)));const audio=activeAudio();if(audio)audio.volume=volume;setSettings(s=>({...s,volume}));try{await api.patchSettings({volume})}catch{}}

  const runScan=async mode=>{
    setScan(s=>({...s,running:true,phase:'starting',errors:0}))
    try{
      let result
      if(mode==='system')result=await api.scanSystem()
      else if(mode==='auto')result=await api.autoScanMusic()
      else if(mode==='pick')result=await api.pickAndScanFolder()
      else result=await api.scan()
      setScan(result||{running:false,phase:'done'})
      if(result?.cancelled)return
      toast(fa?`اسکن کامل شد · ${result?.found||0} فایل · ${result?.updated||0} بروزرسانی`:`Scan complete · ${result?.found||0} files · ${result?.updated||0} updated`)
      await refresh()
      window.dispatchEvent(new CustomEvent('neo:library-refresh'))
    }catch(error){setScan(s=>({...s,running:false,phase:'error',errors:(s.errors||0)+1}));toast(`${fa?'خطای اسکن':'Scan error'}: ${error?.message||error}`)}
  }

  const core=settingsHost?<section className="neo-core-settings" dir={fa?'rtl':'ltr'}>
    <div className="neo-core-head"><div><img src="/neo-logo.png?v=061" alt="NEO"/><span><b>NEO CORE</b><small>{capabilities.nativeCore?(fa?'هسته مستقیم پایتون فعال':'Direct Python core active'):(fa?'حالت سازگار':'Compatibility mode')}</small></span></div><Zap size={20}/></div>
    <div className="neo-core-grid">
      <button onClick={()=>runScan('pick')} disabled={scan.running}><FolderSearch/><b>{fa?'پوشه + اسکن':'Folder + scan'}</b><small>{fa?'انتخاب مستقیم ویندوز':'Native Windows picker'}</small></button>
      <button onClick={()=>runScan('folders')} disabled={scan.running}><ScanLine/><b>{fa?'اسکن پوشه‌ها':'Scan folders'}</b><small>{fa?'هسته Python چندریسمانی':'Parallel Python core'}</small></button>
      <button onClick={()=>runScan('auto')} disabled={scan.running}><Sparkles/><b>{fa?'اسکن هوشمند':'Smart scan'}</b><small>Music · Downloads · Desktop</small></button>
      <button onClick={()=>runScan('system')} disabled={scan.running}><HardDrive/><b>{fa?'اسکن کل سیستم':'Whole system scan'}</b><small>{fa?'بدون مجوز مرورگر':'No browser permission'}</small></button>
      <button onClick={openCinema}><Maximize2/><b>{fa?'سینما تمام‌صفحه':'Fullscreen cinema'}</b><small>F11</small></button>
      <button onClick={()=>refresh()}><RefreshCw/><b>{fa?'بازخوانی هسته':'Refresh core'}</b><small>{fa?'کتابخانه و تنظیمات':'Library and settings'}</small></button>
    </div>
    <div className={`neo-scan-meter ${scan.running?'running':''}`}><div><Gauge size={16}/><span>{scan.running?(fa?`در حال اسکن · ${scan.found||0} فایل`:`Scanning · ${scan.found||0} files`):(fa?`آخرین اسکن · ${scan.found||0} فایل`:`Last scan · ${scan.found||0} files`)}</span></div><div className="neo-scan-line"><i style={{width:scan.running?`${Math.min(96,12+((scan.processed||0)%84))}%`:'100%'}}/></div><div className="neo-scan-stats"><span>{fa?'بروزرسانی':'Updated'} {scan.updated||0}</span><span>{fa?'بدون تغییر':'Unchanged'} {scan.unchanged||0}</span><span>{fa?'خطا':'Errors'} {scan.errors||0}</span></div></div>
  </section>:null

  return <>
    {settingsHost&&createPortal(core,settingsHost)}
    {cinema&&<div className="neo-cinema" dir={fa?'rtl':'ltr'}><div className="neo-cinema-backdrop" style={{backgroundImage:current?`url(/cover/${current.id})`:'none'}}/><div className="neo-cinema-noise"/><header className="neo-cinema-head"><div className="neo-cinema-brand"><img src="/neo-logo.png?v=061" alt="NEO"/><span><b>NEO PLAYER</b><small>{fa?'حالت سینمایی':'Cinema mode'}</small></span></div><div className="neo-cinema-badges"><span><Zap size={14}/>PYTHON CORE</span><span><Waves size={14}/>IMMERSIVE</span></div><button onClick={closeCinema}><Minimize2/></button></header><main className="neo-cinema-stage"><div className={`neo-cinema-art ${playing?'playing':''}`}>{current?<img src={`/cover/${current.id}`} alt=""/>:<div><Music2 size={80}/></div>}<div className="neo-cinema-orbit"/><div className="neo-cinema-orbit two"/></div><div className="neo-cinema-info"><p>{current?.album||(fa?'کتابخانه محلی':'Local library')}</p><h1>{current?.title||(fa?'یک آهنگ پخش کن':'Play something')}</h1><h2>{current?.artist||'NEO Player'}</h2><div className={`neo-cinema-bars ${playing?'playing':''}`}>{Array.from({length:32},(_,i)=><i key={i} style={{'--n':i}}/>)}</div><div className="neo-cinema-progress"><span>{fmt(position)}</span><input type="range" min="0" max={Math.max(duration,1)} step="0.1" value={Math.min(position,Math.max(duration,1))} onChange={e=>seek(e.target.value)}/><span>{fmt(duration)}</span></div><div className="neo-cinema-controls"><button onClick={()=>dispatch('ArrowLeft',true)}><SkipBack/></button><button className="main" onClick={()=>dispatch('Space')}>{playing?<Pause/>:<Play fill="currentColor"/>}</button><button onClick={()=>dispatch('ArrowRight',true)}><SkipForward/></button></div><div className="neo-cinema-volume"><Volume2 size={18}/><input type="range" min="0" max="1" step="0.01" value={settings.volume??.82} onChange={e=>setVolume(e.target.value)}/></div></div></main></div>}
    {message&&<div className="neo-immersive-toast">{message}</div>}
  </>
}
