import{useEffect,useRef}from'react'

export default function PerformanceCore(){
  const warmed=useRef(new Set())
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
        const controller=new AbortController()
        const res=await fetch(`/media/${id}`,{headers:{Range:'bytes=0-196607'},cache:'force-cache',signal:controller.signal})
        const reader=res.body?.getReader?.()
        if(reader){await reader.read();await reader.cancel()}else controller.abort()
      }catch{}
    }
    const hover=e=>{const row=e.target.closest?.('.track-row[data-neo-song-id]');if(row)warm(row.dataset.neoSongId)}
    const down=e=>{const row=e.target.closest?.('.track-row[data-neo-song-id]');if(row)warm(row.dataset.neoSongId)}
    document.addEventListener('pointerover',hover,{passive:true,capture:true})
    document.addEventListener('pointerdown',down,{passive:true,capture:true})
    return()=>{document.removeEventListener('pointerover',hover,true);document.removeEventListener('pointerdown',down,true)}
  },[])

  useEffect(()=>{
    let scheduled=false
    const optimize=()=>{
      scheduled=false
      document.querySelectorAll('img').forEach(img=>{if(!img.closest('.playerbar,.neo-cinema')){img.loading='lazy';img.decoding='async'}})
    }
    const schedule=()=>{if(scheduled)return;scheduled=true;(window.requestIdleCallback||window.requestAnimationFrame)(optimize)}
    schedule()
    const mo=new MutationObserver(schedule)
    mo.observe(document.body,{childList:true,subtree:true})
    return()=>mo.disconnect()
  },[])
  return null
}
