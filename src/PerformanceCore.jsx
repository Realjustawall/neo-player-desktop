import{useEffect,useRef}from'react'
import{api}from'./api'

export default function PerformanceCore(){
  const warmed=useRef(new Set())

  useEffect(()=>{
    const original=api.patchSettings?.bind(api)
    if(!original||api.__neoBatchedSettings)return
    let timer=0,pending={},waiters=[]
    api.__neoBatchedSettings=true
    api.patchSettings=patch=>new Promise((resolve,reject)=>{
      pending={...pending,...patch}
      waiters.push([resolve,reject])
      clearTimeout(timer)
      timer=setTimeout(async()=>{
        const batch=pending,list=waiters
        pending={};waiters=[]
        try{const result=await original(batch);list.forEach(([ok])=>ok(result))}
        catch(error){list.forEach(([,bad])=>bad(error))}
      },90)
    })
    return()=>{clearTimeout(timer);api.patchSettings=original;delete api.__neoBatchedSettings}
  },[])

  useEffect(()=>{
    const setAudio=()=>document.querySelectorAll('audio').forEach(a=>{a.preload='auto'})
    setAudio()
    const mo=new MutationObserver(setAudio)
    mo.observe(document.body,{childList:true,subtree:true})
    return()=>mo.disconnect()
  },[])

  useEffect(()=>{
    let timer=0
    const onScroll=()=>{
      document.documentElement.classList.add('neo-runtime-scroll')
      clearTimeout(timer)
      timer=setTimeout(()=>document.documentElement.classList.remove('neo-runtime-scroll'),110)
    }
    const root=document.querySelector('.content')
    root?.addEventListener('scroll',onScroll,{passive:true})
    return()=>{root?.removeEventListener('scroll',onScroll);clearTimeout(timer)}
  },[])

  useEffect(()=>{
    const warm=async id=>{
      id=Number(id)
      if(!id||warmed.current.has(id))return
      warmed.current.add(id)
      if(warmed.current.size>24){const first=warmed.current.values().next().value;warmed.current.delete(first)}
      try{
        const res=await fetch(`/media/${id}`,{headers:{Range:'bytes=0-262143'},cache:'force-cache'})
        const reader=res.body?.getReader?.()
        if(reader){await reader.read();await reader.cancel()}
      }catch{}
    }
    const prewarm=e=>{const row=e.target.closest?.('.track-row[data-neo-song-id]');if(row)warm(row.dataset.neoSongId)}
    document.addEventListener('pointerover',prewarm,{passive:true,capture:true})
    document.addEventListener('pointerdown',prewarm,{passive:true,capture:true})
    return()=>{document.removeEventListener('pointerover',prewarm,true);document.removeEventListener('pointerdown',prewarm,true)}
  },[])

  useEffect(()=>{
    let scheduled=false
    const optimize=()=>{
      scheduled=false
      document.querySelectorAll('img').forEach(img=>{if(!img.closest('.playerbar,.neo-cinema')){img.loading='lazy';img.decoding='async';img.fetchPriority='low'}})
    }
    const schedule=()=>{if(scheduled)return;scheduled=true;(window.requestIdleCallback||window.requestAnimationFrame)(optimize)}
    schedule()
    const mo=new MutationObserver(schedule)
    mo.observe(document.body,{childList:true,subtree:true})
    return()=>mo.disconnect()
  },[])
  return null
}
