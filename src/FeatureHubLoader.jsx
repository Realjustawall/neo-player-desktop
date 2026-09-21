import React,{useEffect,useState}from'react'
import{Sparkles}from'lucide-react'

export default function FeatureHubLoader(){
  const[Hub,setHub]=useState(null)
  const[loading,setLoading]=useState(false)
  const open=async()=>{
    if(loading)return
    setLoading(true)
    try{
      const mod=await import('./FeatureHub')
      setHub(()=>mod.default)
      let tries=0
      const trigger=()=>{
        const button=document.querySelector('.neo-plus-fab')
        if(button){button.click();return}
        if(tries++<20)requestAnimationFrame(trigger)
      }
      requestAnimationFrame(trigger)
    }finally{setLoading(false)}
  }
  useEffect(()=>{
    if(!Hub)return
    const close=e=>{
      if(!e.target.closest?.('.neo-plus-fab'))return
      setTimeout(()=>{if(!document.querySelector('.neo-plus-panel'))setHub(null)},250)
    }
    document.addEventListener('click',close,true)
    return()=>document.removeEventListener('click',close,true)
  },[Hub])
  if(Hub)return <Hub/>
  return <button className="neo-plus-loader" onClick={open} disabled={loading} title="NEO+"><Sparkles/><span>NEO+</span></button>
}
