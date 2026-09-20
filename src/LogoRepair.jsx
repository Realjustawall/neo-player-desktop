import { useEffect } from 'react'

export default function LogoRepair(){
  useEffect(()=>{
    const apply=()=>{
      document.querySelectorAll('img.neo-logo,img[src="/neo-logo.png"],img[src^="/neo-logo.png?"],img[src="/neo-logo.webp"],img[src^="/neo-logo.webp?"]').forEach(img=>{
        const expected='/neo-logo.webp?v=062'
        if(img.getAttribute('src')!==expected)img.setAttribute('src',expected)
        img.style.objectFit='contain'
      })
      const favicon=document.querySelector('link[rel="icon"]')
      if(favicon){favicon.setAttribute('href','/neo-logo.webp?v=062');favicon.setAttribute('type','image/webp')}
    }
    apply()
    const observer=new MutationObserver(apply)
    observer.observe(document.body,{childList:true,subtree:true})
    return()=>observer.disconnect()
  },[])
  return null
}