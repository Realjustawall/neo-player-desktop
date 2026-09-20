import { useEffect } from 'react'

export default function LogoRepair(){
  useEffect(()=>{
    const apply=()=>{
      document.querySelectorAll('img.neo-logo,img[src="/neo-logo.png"],img[src^="/neo-logo.png?"]').forEach(img=>{
        const expected='/neo-logo.png?v=061'
        if(img.getAttribute('src')!==expected)img.setAttribute('src',expected)
        img.style.objectFit='contain'
      })
    }
    apply()
    const observer=new MutationObserver(apply)
    observer.observe(document.body,{childList:true,subtree:true})
    return()=>observer.disconnect()
  },[])
  return null
}
