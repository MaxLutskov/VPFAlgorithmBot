import { useCallback, useEffect, useState } from 'react'

declare global { interface Window { Telegram?: { WebApp?: { initData?: string, ready: () => void, expand: () => void } } } }
type Incident = { id:number, chatId:number, objectId:number, categoryId:number, algorithmRuleId:number, startedAtUtc:string, endedAtUtc:string|null, problemState:string, answerState:string, firstResponseAtUtc:string|null, responseCount:number, quality:string }
type Detail = Incident & { responses:{id:number, author:string, text:string, createdAtUtc:string}[] }
type Catalog = { chats:Record<string,unknown>[],objects:Record<string,unknown>[],categories:Record<string,unknown>[],algorithms:Record<string,unknown>[],routes:Record<string,unknown>[],templates:Record<string,unknown>[],templateVersions:Record<string,unknown>[],users:Record<string,unknown>[],scopes:Record<string,unknown>[],settings:{key:string,value:string}[],reviews:Record<string,unknown>[],outbox:Record<string,unknown>[],audit:Record<string,unknown>[] }
const demoId = new URLSearchParams(location.search).get('demoUser') || '1'
let adminToken = sessionStorage.getItem('vpfAdminToken') || ''
async function api<T>(path:string, method='GET', body?:unknown):Promise<T> {
  const r = await fetch('/api/miniapp'+path,{method,headers:{'Content-Type':'application/json','X-Telegram-Init-Data':window.Telegram?.WebApp?.initData || '', 'X-Demo-User-Id':demoId,'X-Admin-Token':adminToken},body:body===undefined?undefined:JSON.stringify(body)})
  if(!r.ok) throw new Error((await r.text()).slice(0,300))
  return r.json()
}
const date=(x:string|null)=>x?new Intl.DateTimeFormat('uk-UA',{timeZone:'Europe/Kyiv',dateStyle:'short',timeStyle:'medium'}).format(new Date(x)):'—'
const kyivToday=()=>new Intl.DateTimeFormat('en-CA',{timeZone:'Europe/Kyiv',year:'numeric',month:'2-digit',day:'2-digit'}).format(new Date())
const state=(x:string)=>x==='Active'?'Активний':x==='Resolved'?'Завершений':x==='Answered'?'Відповідь надана':'Без відповіді'

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
    spec.forEach(([key,type])=>{const value=form[key];body[key]=type==='number'?(value?Number(value):null):type==='bool'?value!=='false':value||null})
    try{await api('/admin/'+formType+(editId?'/'+editId:''),editId?'PUT':'POST',body);setForm({});setEditId(null);setCatalog(await api<Catalog>('/admin/catalog'));setNotice('Збережено')}catch(e){setNotice(String(e))}
  }
  async function approve(u:Record<string,unknown>,status:string){try{await api('/admin/users/'+u.id,'PUT',{displayName:u.displayName,role:u.role,status});setCatalog(await api<Catalog>('/admin/catalog'))}catch(e){setNotice(String(e))}}
  async function editUser(u:Record<string,unknown>){const displayName=prompt('Службове ім’я',String(u.displayName));if(!displayName)return;const role=prompt('Роль: admin, operator, viewer',String(u.role));if(!role)return;try{await api('/admin/users/'+u.id,'PUT',{displayName,role,status:u.status});setCatalog(await api<Catalog>('/admin/catalog'))}catch(e){setNotice(String(e))}}
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
        <p>{lookups.objects.find(x=>x.id===i.objectId)?.name||`Об’єкт №${i.objectId}`} · {lookups.algorithms.find(x=>x.id===i.algorithmRuleId)?.name||`Тип №${i.algorithmRuleId}`}</p><small>Початок: {date(i.startedAtUtc)}</small><small>Завершення: {date(i.endedAtUtc)}</small><div className="card-bottom"><span className={i.answerState==='Unanswered'?'unanswered':''}>{state(i.answerState)}</span><span>Відповідей: {i.responseCount}</span></div>
      </button>)}</div>
    </>}
    {tab==='admin'&&<section className="admin"><h2>Адміністрування</h2>{!catalog?<div className="unlock"><p>Введіть пароль адміністратора.</p><input type="password" value={password} onChange={e=>setPassword(e.target.value)} placeholder="Пароль"/><button onClick={()=>void unlock()}>Увійти</button></div>:<>
      <label>Довідник<select value={formType} onChange={e=>{setFormType(e.target.value);setForm({});setEditId(null)}}>{Object.keys(specs).map(x=><option key={x} value={x}>{labels[x]}</option>)}</select></label>
      <div className="admin-grid"><div className="panel"><h3>{editId?`Редагувати №${editId}`:'Додати запис'}</h3>{specs[formType].map(([key,type])=><label key={key}>{key==='algorithmRuleId'?'Алгоритм':key}{key==='algorithmRuleId'?<select value={form[key]??''} disabled={editId!==null && catalog.templates.some(x=>x.id===editId && x.algorithmRuleId!=null)} onChange={e=>setForm({...form,[key]:e.target.value})}><option value="">Оберіть алгоритм</option>{catalog.algorithms.map(rule=><option key={String(rule.id)} value={String(rule.id)}>{String(rule.name)} · {String(catalog.objects.find(x=>x.id===rule.objectId)?.name??'')}</option>)}</select>:<input value={form[key]??''} onChange={e=>setForm({...form,[key]:e.target.value})} placeholder={type==='number'?'Число або порожньо':type==='bool'?'true / false':''}/>}</label>)}<button onClick={()=>void submit()}>Зберегти</button>{editId&&<button className="secondary" onClick={()=>{setEditId(null);setForm({})}}>Скасувати</button>}</div>
      <div className="panel"><h3>Поточні записи</h3><div className="records">{((catalog[formType as keyof Catalog] || []) as Record<string,unknown>[]).map((row,i)=><div className="record" key={i}>{Object.entries(row).map(([k,v])=><div key={k}><b>{k}</b> {String(v??'—')}</div>)}<div className="actions">{formType!=='scopes'&&<button onClick={()=>{setEditId(Number(row.id));setForm(Object.fromEntries(specs[formType].map(([key])=>[key,String(row[key]??'')])));window.scrollTo({top:0,behavior:'smooth'})}}>Редагувати</button>}{formType==='scopes'&&<button onClick={async()=>{await api(`/admin/scopes/${row.userId}/${row.objectId}`,'DELETE');setCatalog(await api<Catalog>('/admin/catalog'))}}>Прибрати доступ</button>}</div></div>)}</div></div></div>
      <div className="panel"><h3>Користувачі та запити доступу</h3>{catalog.users.map(u=><div className="record" key={String(u.id)}>{String(u.displayName)} · {String(u.role)} · {String(u.status)}<div className="actions"><button onClick={()=>void approve(u,'approved')}>Підтвердити</button><button onClick={()=>void approve(u,'blocked')}>Блокувати</button><button onClick={()=>void editUser(u)}>Ім’я і роль</button></div></div>)}</div>
      <div className="panel"><h3>Нагадування</h3><p>Повторювати повідомлення відповідальним, доки немає відповіді, навіть після завершення алгоритму. 0 вимикає нагадування.</p><input type="number" min="0" max="1440" value={reminder} onChange={e=>setReminder(e.target.value)}/><button onClick={async()=>{try{await api('/admin/reminder','PUT',{minutes:Number(reminder)});setNotice('Інтервал збережено')}catch(e){setNotice(String(e))}}}>Зберегти хвилини</button></div>
      <details><summary>Історія шаблонів ({catalog.templateVersions.length})</summary><pre>{JSON.stringify(catalog.templateVersions,null,2)}</pre></details><details><summary>Повідомлення для перевірки ({catalog.reviews.length})</summary><pre>{JSON.stringify(catalog.reviews,null,2)}</pre></details><details><summary>Журнал доставки ({catalog.outbox.length})</summary><pre>{JSON.stringify(catalog.outbox,null,2)}</pre></details><details><summary>Журнал змін ({catalog.audit.length})</summary><pre>{JSON.stringify(catalog.audit,null,2)}</pre></details>
    </>}</section>}
    {detail&&<div className="overlay" onClick={()=>setDetail(null)}><article className="drawer" onClick={e=>e.stopPropagation()}><button className="close" onClick={()=>setDetail(null)}>Закрити</button><h2>Алгоритм №{detail.id}</h2><p>{state(detail.problemState)} · {state(detail.answerState)}</p><p>Початок: {date(detail.startedAtUtc)}<br/>Завершення: {date(detail.endedAtUtc)}</p><h3>Відповіді</h3>{detail.responses.length===0?<p>Відповідей ще немає. Їх можна надати в чаті з ботом навіть після завершення.</p>:detail.responses.map(r=><div className="response" key={r.id}><b>{r.author}</b><small>{date(r.createdAtUtc)}</small><p>{r.text}</p></div>)}</article></div>}
  </div>
}

const specs:Record<string,[string,string][]>= {
  chats:[['telegramChatId','number'],['senderTelegramId','number'],['name','text'],['enabled','bool']],
  objects:[['code','text'],['name','text'],['enabled','bool']],
  categories:[['name','text'],['enabled','bool']],
  algorithms:[['objectId','number'],['categoryId','number'],['name','text'],['matchPattern','text'],['priority','number'],['enabled','bool']],
  routes:[['chatId','number'],['objectId','number'],['categoryId','number'],['userId','number'],['priority','number'],['enabled','bool']],
  templates:[['algorithmRuleId','number'],['title','text'],['text','text'],['sortOrder','number'],['enabled','bool']],
  scopes:[['userId','number'],['objectId','number']]
}
const labels:Record<string,string>={chats:'Чати',objects:'Об’єкти',categories:'Категорії',algorithms:'Алгоритми',routes:'Відповідальні',templates:'Шаблони відповідей',scopes:'Доступ до об’єктів'}
