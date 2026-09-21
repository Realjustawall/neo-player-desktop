import React,{useEffect,useRef,useState}from'react'
import{Sparkles}from'lucide-react'

export default function FeatureHubLoader(){
  const[Hub,setHub]=useState(null),[loading,setLoading]=useState(false)
  const seenOpen=useRef(false)
  const open=async()=>{
    if(loading)return
    setLoading(true)
    try{
      const mod=await import('./FeatureHub')
      seenOpen.current=false
      setHub(()=>mod.default)
      let tries=0
      const trigger=()=>{
        const button=[...document.querySelectorAll('.neo-plus-fab')].find(x=>!x.classList.contains('neo-plus-loader'))
        if(button){button.click();return}
        if(tries++<24)requestAnimationFrame(trigger)
      }
      requestAnimationFrame(trigger)
    }finally{setLoading(false)}
  }
  useEffect(()=>{
    if(!Hub)return
    const check=()=>{
      const panel=document.querySelector('.neo-plus-panel')
      if(panel)seenOpen.current=true
      else if(seenOpen.current&&document.querySelector('.neo-plus-fab:not(.neo-plus-loader)')){seenOpen.current=false;setHub(null)}
    }
    const observer=new MutationObserver(()=>requestAnimationFrame(check))
    observer.observe(document.body,{childList:true,subtree:true})
    const id=setTimeout(check,300)
    return()=>{observer.disconnect();clearTimeout(id)}
  },[Hub])
  if(Hub)return <Hub/>
  return <button className="neo-plus-fab neo-plus-loader" onClick={open} disabled={loading} title="NEO+"><Sparkles/><span>NEO+</span></button>
}
