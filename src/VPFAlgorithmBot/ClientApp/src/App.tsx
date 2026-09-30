import { useCallback, useEffect, useState } from 'react'
import { readApiResponse } from './apiResponse'
import { AdminCatalog, AdminUsers, AdminLogs, keyLabels, labels, specs, type Catalog } from './AdminCatalog'

declare global { interface Window { Telegram?: { WebApp?: { initData?: string, ready: () => void, expand: () => void } } } }
type Incident = { id:number, chatId:number, objectId:number, categoryId:number, algorithmRuleId:number, startedAtUtc:string, endedAtUtc:string|null, problemState:string, answerState:string, firstResponseAtUtc:string|null, responseCount:number, quality:string }
type Detail = Incident & { responses:{id:number, author:string, text:string, createdAtUtc:string}[] }
const demoId = new URLSearchParams(location.search).get('demoUser') || '1'
let adminToken = sessionStorage.getItem('vpfAdminToken') || ''
async function api<T>(path:string, method='GET', body?:unknown):Promise<T> {
  const r = await fetch('/api/miniapp'+path,{method,headers:{'Content-Type':'application/json','X-Telegram-Init-Data':window.Telegram?.WebApp?.initData || '', 'X-Demo-User-Id':demoId,'X-Admin-Token':adminToken},body:body===undefined?undefined:JSON.stringify(body)})
  return readApiResponse<T>(r)
}
const date=(x:string|null)=>x?new Intl.DateTimeFormat('uk-UA',{timeZone:'Europe/Kyiv',dateStyle:'short',timeStyle:'medium'}).format(new Date(x)):'—'
const kyivToday=()=>new Intl.DateTimeFormat('en-CA',{timeZone:'Europe/Kyiv',year:'numeric',month:'2-digit',day:'2-digit'}).format(new Date())
const qualityLabels:Record<string,string>={historical:'Імпортовано з архіву Telegram',historical_inferred_start:'Архів: початок відновлено за тривалістю',historical_missing_end:'Архів: повідомлення про завершення відсутнє; поточний стан не підтверджено',historical_duration_mismatch:'Архів: час у повідомленнях відрізняється від зазначеної тривалості'}
const quality=(x:string)=>qualityLabels[x]||''
const state=(x:string)=>x==='Active'?'Активний':x==='Resolved'?'Завершений':x==='Answered'?'Відповідь надана':'Без відповіді'
const namePattern=(name:string)=>'^'+name.replace(/[.*+?^${}()|[\]\\]/g,'\\$&')+'$'

export default function App(){
  const [tab,setTab]=useState<'dashboard'|'history'|'admin'>('dashboard')
  const [me,setMe]=useState<{id:number,displayName:string,role:string}|null>(null)
  const [stats,setStats]=useState<Record<string,number>>({})
  const [items,setItems]=useState<Incident[]>([])
  const [detail,setDetail]=useState<Detail|null>(null)
  const [filter,setFilter]=useState('active')
  const [period,setPeriod]=useState('all')
  const [fromDate,setFromDate]=useState(kyivToday)
  const [toDate,setToDate]=useState(kyivToday)
  const [catalog,setCatalog]=useState<Catalog|null>(null)
  const [password,setPassword]=useState('')
  const [notice,setNotice]=useState('')
  const [busy,setBusy]=useState(false)
  const [formType,setFormType]=useState('chats')
  const [form,setForm]=useState<Record<string,string>>({})
  const [editId,setEditId]=useState<number|null>(null)
  const [lookups,setLookups]=useState<{objects:{id:number,name:string,code:string}[],algorithms:{id:number,name:string}[]}>({objects:[],algorithms:[]})
  const [reminder,setReminder]=useState('0')

  const load=useCallback(async()=>{
    setBusy(true)
    try{
      const user=await api<{id:number,displayName:string,role:string}>('/me'); setMe(user)
      const params=new URLSearchParams({period})
      if(period==='range'){params.set('fromDate',fromDate);params.set('toDate',toDate)}
      setStats(await api<Record<string,number>>('/dashboard?'+params))
      setLookups(await api('/lookups'))
      if(tab==='dashboard'||filter==='active')params.set('state','active')
      else if(filter==='unanswered')params.set('answer','unanswered')
      setItems(await api<Incident[]>('/incidents?'+params))
      if(tab==='admin' && adminToken) {const c=await api<Catalog>('/admin/catalog');setCatalog(c);setReminder(c.settings.find(x=>x.key==='reminder_minutes')?.value||'0')}
      setNotice('')
    }catch(e){setItems([]);setStats({});setNotice(String(e))}finally{setBusy(false)}
  },[filter,tab,period,fromDate,toDate])
  useEffect(()=>{window.Telegram?.WebApp?.ready();window.Telegram?.WebApp?.expand();void load()},[load])

  async function unlock(){try{const result=await api<{token:string}>('/admin/signin','POST',{password});adminToken=result.token;sessionStorage.setItem('vpfAdminToken',adminToken);const c=await api<Catalog>('/admin/catalog');setCatalog(c);setReminder(c.settings.find(x=>x.key==='reminder_minutes')?.value||'0');setNotice('Адміністративний доступ відкрито')}catch(e){setNotice(String(e))}}
  async function submit(){
    const spec=specs[formType];if(!spec)return
    const body:Record<string,unknown>={}
    try{
      spec.forEach(([key,type])=>{
        const value=(form[key]??'').trim()
        if(type==='number'){
          if(!value){
            if(key==='senderTelegramId')body[key]=0
            else if(key==='priority'||key==='sortOrder')body[key]=100
            else if(formType==='routes' && ['chatId','objectId','categoryId'].includes(key))body[key]=null
            else throw new Error(`Заповніть поле «${keyLabels[key]||key}».`)
          } else {const number=Number(value);if(!Number.isSafeInteger(number))throw new Error(`Поле «${keyLabels[key]||key}»: введіть ціле число.`);body[key]=number}
        } else if(type==='bool') {
          if(value && !['true','false'].includes(value))throw new Error(`Поле «${keyLabels[key]||key}»: оберіть так або ні.`)
          body[key]=value!=='false'
        } else if(key==='matchPattern'&&formType==='algorithms') {
          const previous=catalog?.algorithms.find(x=>x.id===editId)
          body[key]=!value || previous && value===namePattern(String(previous.name))
            ? namePattern(String(body.name||'')) : value
        } else {if(!value)throw new Error(`Заповніть поле «${keyLabels[key]||key}».`);body[key]=value}
      })
      await api('/admin/'+formType+(editId?'/'+editId:''),editId?'PUT':'POST',body);setForm({});setEditId(null);setCatalog(await api<Catalog>('/admin/catalog'));setNotice('Збережено')
    }catch(e){setNotice(String(e))}
  }
  async function approve(u:Record<string,unknown>,status:string){try{await api('/admin/users/'+u.id,'PUT',{displayName:u.displayName,role:u.role,status});setCatalog(await api<Catalog>('/admin/catalog'))}catch(e){setNotice(String(e))}}
  async function saveUser(u:Record<string,unknown>,displayName:string,role:string){try{if(!displayName.trim())throw new Error('Введіть службове ім’я.');await api('/admin/users/'+u.id,'PUT',{displayName:displayName.trim(),role,status:u.status});setCatalog(await api<Catalog>('/admin/catalog'));setNotice('Користувача оновлено')}catch(e){setNotice(String(e))}}
  return <div className="shell">
    <header><div><p className="eyebrow">VPF ALGORITHM BOT</p><h1>Виробничі алгоритми</h1><p>{me?.displayName || 'Завантаження облікового запису'}</p></div><button className="refresh" onClick={()=>void load()}>{busy?'Оновлення…':'Оновити'}</button></header>
    <nav><button className={tab==='dashboard'?'selected':''} onClick={()=>setTab('dashboard')}>Дешборд</button><button className={tab==='history'?'selected':''} onClick={()=>{setFilter('all');setTab('history')}}>Історія</button>{me?.role==='admin'&&<button className={tab==='admin'?'selected':''} onClick={()=>setTab('admin')}>Адмінпанель</button>}</nav>
    {notice&&<p className="notice">{notice}</p>}
    {tab!=='admin'&&<>
      <section className="period-bar"><label>Період<select value={period} onChange={e=>setPeriod(e.target.value)}><option value="today">Сьогодні</option><option value="yesterday">Вчора</option><option value="range">За датами</option><option value="all">За весь час</option></select></label>{period==='range'&&<><label>Від<input type="date" value={fromDate} onChange={e=>setFromDate(e.target.value)}/></label><label>До<input type="date" value={toDate} onChange={e=>setToDate(e.target.value)}/></label></>}<small>За датою початку алгоритму, час Києва</small></section>
      <section className="metrics"><div><strong>{stats.active??0}</strong><span>Активні</span></div><div><strong>{stats.unanswered??0}</strong><span>Без відповіді</span></div><div><strong>{stats.resolvedUnanswered??0}</strong><span>Завершені без відповіді</span></div></section>
      <section className="section-head"><h2>{tab==='dashboard'?'Активні алгоритми':'Історія алгоритмів'}</h2><select value={tab==='dashboard'?'active':filter} onChange={e=>setFilter(e.target.value)} disabled={tab==='dashboard'}><option value="active">Активні</option><option value="unanswered">Без відповіді</option><option value="all">Усі</option></select></section>
      <div className="cards">{items.length===0?<p className="empty">Немає алгоритмів за обраним фільтром.</p>:items.map(i=><button className="card" key={i.id} onClick={async()=>{try{setDetail(await api<Detail>('/incidents/'+i.id))}catch(e){setNotice(String(e))}}}>
        <div className="card-top"><b>Алгоритм №{i.id}</b><span className={i.problemState==='Active'?'badge active':'badge'}>{state(i.problemState)}</span></div>
        <p>{lookups.objects.find(x=>x.id===i.objectId)?.name||`Об’єкт №${i.objectId}`} · {lookups.algorithms.find(x=>x.id===i.algorithmRuleId)?.name||`Тип №${i.algorithmRuleId}`}</p><small>Початок: {date(i.startedAtUtc)}</small><small>Завершення: {date(i.endedAtUtc)}</small>{quality(i.quality)&&<small>{quality(i.quality)}</small>}<div className="card-bottom"><span className={i.answerState==='Unanswered'?'unanswered':''}>{state(i.answerState)}</span><span>Відповідей: {i.responseCount}</span></div>
      </button>)}</div>
    </>}
    {tab==='admin'&&<section className="admin"><h2>Адміністрування</h2>{!catalog?<div className="unlock"><p>Введіть пароль адміністратора.</p><input type="password" value={password} onChange={e=>setPassword(e.target.value)} placeholder="Пароль"/><button onClick={()=>void unlock()}>Увійти</button></div>:<>
      <label>Довідник<select value={formType} onChange={e=>{setFormType(e.target.value);setForm({});setEditId(null)}}>{Object.keys(specs).map(x=><option key={x} value={x}>{labels[x]}</option>)}</select></label>
      <AdminCatalog catalog={catalog} formType={formType} form={form} setForm={setForm} editId={editId} onSubmit={()=>void submit()}
        onEdit={row=>{setEditId(Number(row.id));setForm(Object.fromEntries(specs[formType].map(([key])=>[key,String(row[key]??'')])));window.scrollTo({top:0,behavior:'smooth'})}}
        onCancel={()=>{setEditId(null);setForm({})}}
        onRemoveScope={row=>{void (async()=>{try{await api(`/admin/scopes/${row.userId}/${row.objectId}`,'DELETE');setCatalog(await api<Catalog>('/admin/catalog'));setNotice('Доступ прибрано')}catch(e){setNotice(String(e))}})()}} />
      <AdminUsers catalog={catalog} onStatus={(user,status)=>void approve(user,status)} onSave={(user,displayName,role)=>void saveUser(user,displayName,role)} />
      <div className="panel"><h3>Нагадування</h3><p>Повторювати повідомлення відповідальним, доки немає відповіді, навіть після завершення алгоритму. 0 вимикає нагадування.</p><input type="number" min="0" max="1440" value={reminder} onChange={e=>setReminder(e.target.value)}/><button onClick={async()=>{try{await api('/admin/reminder','PUT',{minutes:Number(reminder)});setNotice('Інтервал збережено')}catch(e){setNotice(String(e))}}}>Зберегти хвилини</button></div>
      <AdminLogs catalog={catalog}/>
    </>}</section>}
    {detail&&<div className="overlay" onClick={()=>setDetail(null)}><article className="drawer" onClick={e=>e.stopPropagation()}><button className="close" onClick={()=>setDetail(null)}>Закрити</button><h2>Алгоритм №{detail.id}</h2><p>{state(detail.problemState)} · {state(detail.answerState)}</p><p>Початок: {date(detail.startedAtUtc)}<br/>Завершення: {date(detail.endedAtUtc)}</p>{quality(detail.quality)&&<p className="notice">{quality(detail.quality)}</p>}<h3>Відповіді</h3>{detail.responses.length===0?<p>Відповідей ще немає. Їх можна надати в чаті з ботом навіть після завершення.</p>:detail.responses.map(r=><div className="response" key={r.id}><b>{r.author}</b><small>{date(r.createdAtUtc)}</small><p>{r.text}</p></div>)}</article></div>}
  </div>
}
