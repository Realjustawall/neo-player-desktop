import React,{useEffect,useMemo,useRef,useState}from'react'
import{createPortal}from'react-dom'
import{FolderOpen,FolderSearch,Heart,ListPlus,Search,Trash2,X}from'lucide-react'
import{api}from'./api'

const norm=v=>String(v||'').normalize('NFKD').replace(/[\u064B-\u065F\u0670\u06D6-\u06ED]/g,'').replace(/[يى]/g,'ی').replace(/ك/g,'ک').toLocaleLowerCase().trim()
const cmp=(a,b)=>String(a||'').localeCompare(String(b||''),undefined,{sensitivity:'base',numeric:true})

export default function LibraryBoost(){
  const[host,setHost]=useState(null),[songs,setSongs]=useState([]),[selected,setSelected]=useState(new Set()),[query,setQuery]=useState(''),[artist,setArtist]=useState(''),[album,setAlbum]=useState(''),[genre,setGenre]=useState(''),[sort,setSort]=useState('artist'),[menu,setMenu]=useState(null),[busy,setBusy]=useState(false),[lang,setLang]=useState('fa')
  const lastIndex=useRef(-1),raf=useRef(0)
  const fa=lang!=='en'
  const ordered=useMemo(()=>{const x=[...songs];if(sort==='title')x.sort((a,b)=>cmp(a.title,b.title));else if(sort==='album')x.sort((a,b)=>cmp(a.album,b.album)||(a.track||0)-(b.track||0));else if(sort==='added')x.sort((a,b)=>(b.addedAt||0)-(a.addedAt||0));else if(sort==='duration')x.sort((a,b)=>(b.duration||0)-(a.duration||0));else x.sort((a,b)=>cmp(a.artist,b.artist)||cmp(a.album,b.album)||(a.track||0)-(b.track||0));return x},[songs,sort])
  const filters=useMemo(()=>({artists:[...new Set(songs.map(s=>s.artist).filter(Boolean))].sort(cmp),albums:[...new Set(songs.map(s=>s.album).filter(Boolean))].sort(cmp),genres:[...new Set(songs.map(s=>s.genre).filter(Boolean))].sort(cmp)}),[songs])
  const visible=useMemo(()=>{const q=norm(query);return ordered.filter(s=>(!artist||s.artist===artist)&&(!album||s.album===album)&&(!genre||s.genre===genre)&&(!q||norm([s.title,s.artist,s.album,s.genre,s.year].join(' ')).includes(q)))},[ordered,query,artist,album,genre])
  const visibleIds=useMemo(()=>new Set(visible.map(s=>Number(s.id))),[visible])

  const load=async()=>{try{const[saved,items]=await Promise.all([api.settings(),api.library()]);setLang(saved.language||'fa');setSongs(items)}catch{}}
  useEffect(()=>{load()},[])

  useEffect(()=>{
    const sync=()=>{
      raf.current=0
      const page=document.querySelector('.page'),tabs=page?.querySelector(':scope > .tabs'),table=page?.querySelector(':scope > .track-table')
      let target=document.getElementById('neo-library-boost-host')
      if(tabs&&table){
        if(!target){target=document.createElement('div');target.id='neo-library-boost-host';table.before(target)}
        if(host!==target)setHost(target)
        const select=page.querySelector('.section-title .toolbar select')
        if(select?.value&&select.value!==sort)setSort(select.value)
        const rows=table.querySelectorAll(':scope > .track-row')
        rows.forEach((row,i)=>{const song=ordered[i];if(!song)return;const id=Number(song.id);row.dataset.neoSongId=String(id);row.classList.toggle('neo-filter-hidden',!visibleIds.has(id));row.classList.toggle('neo-selected',selected.has(id));const img=row.querySelector('img');if(img){img.loading='lazy';img.decoding='async';img.fetchPriority='low'}})
      }else{
        if(target)target.remove()
        if(host)setHost(null)
      }
    }
    const schedule=()=>{if(!raf.current)raf.current=requestAnimationFrame(sync)}
    schedule()
    const page=document.querySelector('.page')||document.body
    const mo=new MutationObserver(schedule)
    mo.observe(page,{subtree:true,childList:true})
    return()=>{mo.disconnect();if(raf.current)cancelAnimationFrame(raf.current)}
  },[ordered,visibleIds,selected,sort,host])

  useEffect(()=>{
    const click=e=>{const row=e.target.closest?.('.track-row[data-neo-song-id]');if(!row||e.target.closest('button,input,select,a'))return;const id=Number(row.dataset.neoSongId),rows=[...row.parentElement.querySelectorAll('.track-row[data-neo-song-id]:not(.neo-filter-hidden)')],idx=rows.indexOf(row);setSelected(prev=>{const next=new Set(e.ctrlKey||e.metaKey?prev:[]);if(e.shiftKey&&lastIndex.current>=0){const a=Math.min(lastIndex.current,idx),b=Math.max(lastIndex.current,idx);for(let i=a;i<=b;i++)next.add(Number(rows[i].dataset.neoSongId))}else if(next.has(id)&&(e.ctrlKey||e.metaKey))next.delete(id);else next.add(id);lastIndex.current=idx;return next})}
    const context=e=>{const row=e.target.closest?.('.track-row[data-neo-song-id]');if(!row)return;e.preventDefault();const id=Number(row.dataset.neoSongId);setSelected(prev=>prev.has(id)?prev:new Set([id]));setMenu({x:e.clientX,y:e.clientY})}
    document.addEventListener('click',click,true);document.addEventListener('contextmenu',context,true)
    return()=>{document.removeEventListener('click',click,true);document.removeEventListener('contextmenu',context,true)}
  },[])

  const ids=()=>[...selected]
  const pickScan=async()=>{setBusy(true);try{const r=await api.pickAndScanFolder();if(!r?.cancelled)await load()}finally{setBusy(false)}}
  const hideSelected=async()=>{const list=ids();if(!list.length)return;setBusy(true);try{const fn=window.pywebview?.api?.native_hide_songs;if(typeof fn==='function')await fn(list,true);else await Promise.all(list.map(id=>api.hideSong(id,true)));setSongs(x=>x.filter(s=>!list.includes(Number(s.id))));setSelected(new Set())}finally{setBusy(false)}}
  const deleteFiles=async()=>{const list=ids();if(!list.length)return;if(!confirm(fa?`حذف دائمی ${list.length} فایل از دیسک؟ این عملیات قابل بازگشت نیست.`:`Permanently delete ${list.length} files from disk? This cannot be undone.`))return;setBusy(true);try{const fn=window.pywebview?.api?.native_delete_song_files;if(typeof fn!=='function')throw new Error('Native delete is unavailable');await fn(list);setSongs(x=>x.filter(s=>!list.includes(Number(s.id))));setSelected(new Set())}catch(e){alert(e.message||e)}finally{setBusy(false)}}
  const reveal=async()=>{const id=ids()[0];if(!id)return;try{await window.pywebview?.api?.reveal_song?.(id)}catch{}}
  const favorite=async()=>{const list=ids();if(!list.length)return;setBusy(true);try{await Promise.all(list.map(id=>api.favorite(id,true)));setSongs(x=>x.map(s=>list.includes(Number(s.id))?{...s,favorite:true}:s));setSelected(new Set())}finally{setBusy(false)}}
  const addQueue=async()=>{const list=ids();if(!list.length)return;const current=await api.queue();const existing=current.map(x=>Number(x.id)),next=[...existing,...list.filter(id=>!existing.includes(id))];await api.setQueue(next);setSelected(new Set())}
  const clearFilters=()=>{setQuery('');setArtist('');setAlbum('');setGenre('')}

  const bar=host?<div className="neo-library-boost" dir={fa?'rtl':'ltr'}><div className="neo-library-search"><Search size={16}/><input value={query} onChange={e=>setQuery(e.target.value)} placeholder={fa?'فیلتر فوری آهنگ‌ها':'Instant filter'}/>{query&&<button onClick={()=>setQuery('')}><X size={14}/></button>}</div><select value={artist} onChange={e=>setArtist(e.target.value)}><option value="">{fa?'همه هنرمندها':'All artists'}</option>{filters.artists.map(x=><option key={x}>{x}</option>)}</select><select value={album} onChange={e=>setAlbum(e.target.value)}><option value="">{fa?'همه آلبوم‌ها':'All albums'}</option>{filters.albums.map(x=><option key={x}>{x}</option>)}</select><select value={genre} onChange={e=>setGenre(e.target.value)}><option value="">{fa?'همه سبک‌ها':'All genres'}</option>{filters.genres.map(x=><option key={x}>{x}</option>)}</select><span className="neo-library-count">{visible.length}/{songs.length}</span><button onClick={pickScan} disabled={busy}><FolderSearch size={16}/>{fa?'یک فولدر + اسکن':'Folder + scan'}</button>{selected.size>0&&<div className="neo-library-selection"><b>{selected.size}</b><button onClick={favorite} title={fa?'پسندیدن':'Like'}><Heart size={16}/></button><button onClick={addQueue} title={fa?'افزودن به صف':'Add to queue'}><ListPlus size={16}/></button><button onClick={reveal} title={fa?'باز کردن پوشه':'Open folder'}><FolderOpen size={16}/></button><button onClick={hideSelected} title={fa?'حذف از کتابخانه':'Remove from library'}><X size={16}/></button><button className="danger" onClick={deleteFiles} title={fa?'حذف فایل':'Delete file'}><Trash2 size={16}/></button></div>}{(query||artist||album||genre)&&<button className="neo-clear-filter" onClick={clearFilters}>{fa?'پاک کردن فیلتر':'Clear'}</button>}</div>:null
  const context=menu?createPortal(<div className="neo-track-menu" style={{left:Math.min(menu.x,window.innerWidth-220),top:Math.min(menu.y,window.innerHeight-260)}} dir={fa?'rtl':'ltr'} onMouseLeave={()=>setMenu(null)}><button onClick={()=>{reveal();setMenu(null)}}><FolderOpen size={16}/>{fa?'باز کردن محل فایل':'Show in folder'}</button><button onClick={()=>{favorite();setMenu(null)}}><Heart size={16}/>{fa?'افزودن به پسندیده‌ها':'Add to liked'}</button><button onClick={()=>{addQueue();setMenu(null)}}><ListPlus size={16}/>{fa?'افزودن به صف':'Add to queue'}</button><button onClick={()=>{hideSelected();setMenu(null)}}><X size={16}/>{fa?'حذف از کتابخانه':'Remove from library'}</button><button className="danger" onClick={()=>{deleteFiles();setMenu(null)}}><Trash2 size={16}/>{fa?'حذف فایل از دیسک':'Delete file from disk'}</button></div>,document.body):null
  return <>{host&&createPortal(bar,host)}{context}</>
}
