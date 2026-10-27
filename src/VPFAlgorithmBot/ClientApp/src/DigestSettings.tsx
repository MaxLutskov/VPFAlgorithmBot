import { useState } from 'react'
import { objectFamily, type Catalog } from './AdminCatalog'

export function DigestSettings({catalog,onSave,onDisable}: {
  catalog:Catalog,
  onSave:(scheduleId:number|null,chatId:number,localTime:string,objectIds:number[])=>Promise<void>,
  onDisable:(scheduleId:number)=>Promise<void>
}) {
  const [editingId,setEditingId]=useState<number|null>(null)
  const [chatId,setChatId]=useState('')
  const [localTime,setLocalTime]=useState('16:00')
  const [objectIds,setObjectIds]=useState<number[]>([])
  const [busy,setBusy]=useState(false)
  const [error,setError]=useState('')
  const chats=catalog.chats.filter(x=>x.enabled)
  const objects=catalog.objects.filter(x=>x.enabled)
  const activeIds=new Set(objects.map(x=>Number(x.id)))
  const selectedIds=objectIds.filter(id=>activeIds.has(id))
  const grouped=objects.reduce<Record<string,typeof objects>>((groups,x)=>{
    const family=objectFamily(x.code)
    ;(groups[family]??=[]).push(x)
    return groups
  },{})
  const startNew=()=>{
    setEditingId(null)
    setChatId('')
    setLocalTime('16:00')
    setObjectIds([])
    setError('')
  }
  const editSchedule=(schedule:Catalog['digests'][number])=>{
    setEditingId(schedule.id)
    setChatId(String(schedule.chatId))
    setLocalTime(schedule.localTime)
    setObjectIds(schedule.objectIds.filter(id=>activeIds.has(id)))
    setError('')
    document.getElementById('digest-editor')?.scrollIntoView({behavior:'smooth',block:'start'})
  }
  const toggle=(id:number)=>setObjectIds(current=>{
    const selected=current.filter(x=>activeIds.has(x))
    return selected.includes(id)?selected.filter(x=>x!==id):[...selected,id]
  })
  const toggleGroup=(items:typeof objects)=>setObjectIds(current=>{
    const selected=new Set(current.filter(id=>activeIds.has(id)))
    const allSelected=items.every(x=>selected.has(Number(x.id)))
    for(const item of items){if(allSelected)selected.delete(Number(item.id));else selected.add(Number(item.id))}
    return [...selected]
  })
  const save=async()=>{
    if(!chatId||selectedIds.length===0){setError('Оберіть чат і хоча б один активний об’єкт.');return}
    setBusy(true);setError('')
    try{
      await onSave(editingId,Number(chatId),localTime,selectedIds)
      if(editingId===null)startNew();else setObjectIds(selectedIds)
    }catch(e){setError(String(e))}finally{setBusy(false)}
  }
  const disable=async(id:number)=>{
    setBusy(true);setError('')
    try{await onDisable(id);if(editingId===id)startNew()}catch(e){setError(String(e))}finally{setBusy(false)}
  }
  return <div className="panel" id="daily-digests">
    <h3>Щоденні розсилки алгоритмів без відповіді</h3>
    <p>Кожна розсилка має власний чат, час і набір об’єктів. Можна створити кілька розсилок на той самий час або в той самий чат. Час — київський.</p>
    <div className="picker-head" id="digest-editor"><strong>{editingId===null?'Нова розсилка':'Редагування розсилки'}</strong>{editingId!==null&&<button className="secondary" disabled={busy} onClick={startNew}>+ Нова розсилка</button>}</div>
    <label>Чат для розсилки<select value={chatId} disabled={busy} onChange={e=>{setChatId(e.target.value);setError('')}}><option value="">Оберіть чат</option>{chats.map(x=><option key={String(x.id)} value={String(x.id)}>{String(x.name)}</option>)}</select></label>
    <label>Час щодня (Київ)<input type="time" value={localTime} disabled={busy} onChange={e=>setLocalTime(e.target.value)}/></label>
    <div className="picker-head"><strong>Об’єкти ({selectedIds.length})</strong><button className="secondary" disabled={busy} onClick={()=>setObjectIds(objects.map(x=>Number(x.id)))}>Вибрати всі</button><button className="secondary" disabled={busy} onClick={()=>setObjectIds([])}>Очистити</button></div>
    <div className="object-picker">{Object.entries(grouped).map(([family,items])=><fieldset key={family}><legend>{family}</legend>{['РЧВ','ПНС'].includes(family)&&<label className="check-row group-check"><input type="checkbox" checked={items.every(x=>selectedIds.includes(Number(x.id)))} disabled={busy} onChange={()=>toggleGroup(items)}/><strong>Уся група {family} ({items.length})</strong></label>}{items.sort((a,b)=>String(a.name).localeCompare(String(b.name),'uk',{numeric:true})).map(x=><label className="check-row" key={String(x.id)}><input type="checkbox" checked={selectedIds.includes(Number(x.id))} disabled={busy} onChange={()=>toggle(Number(x.id))}/><span>{String(x.name)}</span></label>)}</fieldset>)}</div>
    <div className="actions"><button disabled={busy} onClick={()=>void save()}>{busy?'Зберігаємо…':editingId===null?'Додати розсилку':'Зберегти зміни'}</button></div>
    {error&&<p className="notice" role="alert">{error}</p>}
    <div className="records"><h4>Налаштовані розсилки</h4>{catalog.digests.filter(x=>x.enabled).length===0?<p className="empty">Розсилок поки немає.</p>:catalog.digests.filter(x=>x.enabled).map(x=><div className="record" key={x.id}>
      <strong>{String(catalog.chats.find(c=>Number(c.id)===x.chatId)?.name||'Чат')} · {x.localTime}</strong>
      <p>{x.objectIds.map(id=>String(catalog.objects.find(o=>Number(o.id)===id)?.name||'Невідомий об’єкт')).join(', ')}</p>
      {x.lastDelivery&&<small>Остання розсилка: {x.lastDelivery.localDate} · {x.lastDelivery.status==='sent'?'надіслано':x.lastDelivery.status==='failed'?`помилка: ${x.lastDelivery.lastError||'доставка не вдалася'}`:'очікує надсилання'}</small>}
      <div className="actions"><button className="secondary" disabled={busy} onClick={()=>editSchedule(x)}>Налаштувати</button><button className="danger" disabled={busy} onClick={()=>void disable(x.id)}>Вимкнути</button></div>
    </div>)}</div>
  </div>
}
