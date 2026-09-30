import { useState } from 'react'
import type { Dispatch, SetStateAction } from 'react'

export type Row = Record<string, unknown>
export type Catalog = { chats:Row[],objects:Row[],categories:Row[],algorithms:Row[],routes:Row[],templates:Row[],templateVersions:Row[],users:Row[],scopes:Row[],settings:{key:string,value:string}[],reviews:Row[],outbox:Row[],audit:Row[] }
export const specs:Record<string,[string,string][]>= {
  chats:[['telegramChatId','number'],['senderTelegramId','number'],['name','text'],['enabled','bool']],
  objects:[['code','text'],['name','text'],['enabled','bool']],
  categories:[['name','text'],['enabled','bool']],
  algorithms:[['objectId','number'],['categoryId','number'],['name','text'],['matchPattern','text'],['priority','number'],['enabled','bool']],
  routes:[['chatId','number'],['objectId','number'],['categoryId','number'],['userId','number'],['priority','number'],['enabled','bool']],
  templates:[['algorithmRuleId','number'],['title','text'],['text','text'],['sortOrder','number'],['enabled','bool']],
  scopes:[['userId','number'],['objectId','number']]
}
export const labels:Record<string,string>={chats:'Чати',objects:'Об’єкти',categories:'Категорії',algorithms:'Типи алгоритмів',routes:'Відповідальні',templates:'Шаблони відповідей',scopes:'Доступ до об’єктів'}
export const keyLabels:Record<string,string>={telegramChatId:'Telegram ID чату',senderTelegramId:'Telegram ID бота-відправника',code:'Код об’єкта',name:'Назва',enabled:'Увімкнено',objectId:'Об’єкт',categoryId:'Категорія',userId:'Працівник',chatId:'Чат',priority:'Пріоритет',matchPattern:'Правило розпізнавання',algorithmRuleId:'Тип алгоритму',title:'Назва відповіді',text:'Текст відповіді',sortOrder:'Порядок показу'}
const id=(x:unknown)=>String(x??'')
const find=(rows:Row[], value:unknown)=>rows.find(x=>id(x.id)===id(value))
const name=(rows:Row[], value:unknown, key='name')=>value==null?'':String(find(rows,value)?.[key]??'Невідомий запис')
const familyOptions=(catalog:Catalog)=>{
  const groups=new Map<string,Row>()
  for(const object of catalog.objects){const family=String(object.code).replace(/\s+/g,'').replace(/\d+$/,'')||String(object.code);if(!groups.has(family))groups.set(family,object)}
  return [...groups].map(([family,object])=>({id:id(catalog.algorithms.find(x=>x.family===family)?.objectId??object.id),label:family}))
}
const algorithmName=(catalog:Catalog,value:unknown)=>{const rule=find(catalog.algorithms,value);return rule?`${String(rule.name)} · ${String(rule.family)}`:'Невідомий алгоритм'}

export function AdminCatalog({catalog,formType,form,setForm,editId,onSubmit,onEdit,onCancel,onRemoveScope}: {
  catalog:Catalog,formType:string,form:Record<string,string>,setForm:Dispatch<SetStateAction<Record<string,string>>>,editId:number|null,
  onSubmit:()=>void,onEdit:(row:Row)=>void,onCancel:()=>void,onRemoveScope:(row:Row)=>void
}){
  const options=(key:string):{id:string,label:string}[]|null=>{
    if(key==='objectId'&&formType==='algorithms')return familyOptions(catalog)
    if(key==='chatId')return catalog.chats.filter(x=>x.enabled).map(x=>({id:id(x.id),label:String(x.name)}))
    if(key==='objectId')return catalog.objects.map(x=>({id:id(x.id),label:`${String(x.name)} (${String(x.code)})`}))
    if(key==='categoryId')return catalog.categories.map(x=>({id:id(x.id),label:String(x.name)}))
    if(key==='userId')return catalog.users.map(x=>({id:id(x.id),label:`${String(x.displayName)} · ${roles[String(x.role)]||String(x.role)}`}))
    if(key==='algorithmRuleId')return catalog.algorithms.map(x=>({id:id(x.id),label:`${String(x.name)} · ${String(x.family)}`}))
    return null
  }
  const optional=(key:string)=>formType==='routes'&&['chatId','objectId','categoryId'].includes(key)
  const selectPlaceholder=(key:string)=>optional(key)?`Усі ${key==='chatId'?'чати':key==='objectId'?'об’єкти':'категорії'}`:key==='objectId'&&formType==='algorithms'?'Оберіть групу об’єктів':'Оберіть зі списку'
  const label=(key:string)=>key==='objectId'&&formType==='algorithms'?'Група об’єктів':keyLabels[key]||key
  const value=(row:Row,key:string):string=>{
    const data=row[key]
    if(data==null)return optional(key)?selectPlaceholder(key):'—'
    if(key==='algorithmRuleId')return algorithmName(catalog,data)
    if(key==='chatId')return name(catalog.chats,data)
    if(key==='objectId')return formType==='algorithms'?String(row.family):name(catalog.objects,data)
    if(key==='categoryId')return name(catalog.categories,data)
    if(key==='userId')return name(catalog.users,data,'displayName')
    if(key==='enabled')return data?'Так':'Ні'
    return String(data)
  }
  const title=(row:Row)=>{
    if(formType==='routes')return name(catalog.users,row.userId,'displayName')
    if(formType==='scopes')return `${name(catalog.users,row.userId,'displayName')} · ${name(catalog.objects,row.objectId)}`
    if(formType==='algorithms')return `${String(row.name)} · ${String(row.family)}`
    if(formType==='templates')return `${String(row.title)} · ${algorithmName(catalog,row.algorithmRuleId)}`
    return String(row.name??row.code??'Запис')
  }
  const rows=catalog[formType as keyof Catalog] as Row[]
  return <div className="admin-grid"><div className="panel"><h3>{editId?`Редагувати запис`:'Додати запис'}</h3>
    {specs[formType].map(([key,type])=>{
      if(key==='matchPattern')return <details className="advanced-rule" key={key}><summary>Розширене правило розпізнавання</summary><p>Для звичайного алгоритму залиште порожнім: правило створиться з назви.</p><textarea value={form[key]??''} onChange={e=>setForm(x=>({...x,[key]:e.target.value}))} rows={2}/></details>
      const list=options(key)
      return <label key={key}>{label(key)}
        {list?<select value={form[key]??''} disabled={key==='objectId'&&formType==='algorithms'&&editId!==null || key==='algorithmRuleId'&&formType==='templates'&&editId!==null} onChange={e=>setForm(x=>({...x,[key]:e.target.value}))}>
          <option value="">{selectPlaceholder(key)}</option>{list.map(item=><option key={item.id} value={item.id}>{item.label}</option>)}
        </select>:type==='bool'?<select value={form[key]??'true'} onChange={e=>setForm(x=>({...x,[key]:e.target.value}))}><option value="true">Так</option><option value="false">Ні</option></select>:
          key==='text'||key==='matchPattern'?<textarea value={form[key]??''} onChange={e=>setForm(x=>({...x,[key]:e.target.value}))} rows={key==='text'?4:2}/>:
          <input type={type==='number'?'number':'text'} value={form[key]??''} onChange={e=>setForm(x=>({...x,[key]:e.target.value}))} placeholder={key==='senderTelegramId'?'0 — будь-який відправник':key==='priority'||key==='sortOrder'?'100 за замовчуванням':''}/>}
      </label>
    })}<div className="actions"><button onClick={onSubmit}>Зберегти</button>{editId&&<button className="secondary" onClick={onCancel}>Скасувати</button>}</div></div>
    <div className="panel"><h3>Поточні записи</h3><div className="records">{rows.length===0?<p className="empty">Поки немає записів.</p>:rows.map((row,i)=><div className="record" key={id(row.id??`${row.userId}-${row.objectId}-${i}`)}>
      <strong>{title(row)}</strong><div className="record-fields">{specs[formType].filter(([key])=>!['name','title','matchPattern'].includes(key)).map(([key])=><div key={key}><span>{label(key)}</span><b>{value(row,key)}</b></div>)}</div>
      <div className="actions">{formType==='scopes'?<button onClick={()=>onRemoveScope(row)}>Прибрати доступ</button>:<button onClick={()=>onEdit(row)}>Редагувати</button>}</div>
    </div>)}</div></div></div>
}

const roles:Record<string,string>={admin:'Адміністратор',operator:'Оператор',viewer:'Перегляд'}
const statuses:Record<string,string>={pending:'Очікує підтвердження',approved:'Підтверджено',denied:'Відхилено',blocked:'Заблоковано'}
const deliveryStatuses:Record<string,string>={pending:'Очікує надсилання',sent:'Надіслано',failed:'Помилка доставки',cancelled:'Скасовано'}
const auditActions:Record<string,string>={Added:'Додано',Modified:'Змінено',Deleted:'Видалено','consolidate-algorithms':'Об’єднано алгоритми'}

export function AdminUsers({catalog,onSave,onStatus}: {
  catalog:Catalog,onSave:(user:Row,displayName:string,role:string)=>void,onStatus:(user:Row,status:string)=>void
}){
  const [editing,setEditing]=useState<string|null>(null)
  const [displayName,setDisplayName]=useState('')
  const [role,setRole]=useState('operator')
  return <div className="panel"><h3>Користувачі та запити доступу</h3>{catalog.users.map(user=>
    <div className="record" key={id(user.id)}><strong>{String(user.displayName)}</strong>
      <p>{roles[String(user.role)]||String(user.role)} · {statuses[String(user.status)]||String(user.status)}</p>
      {editing===id(user.id)?<div className="user-edit"><label>Службове ім’я<input value={displayName} onChange={e=>setDisplayName(e.target.value)}/></label><label>Роль<select value={role} onChange={e=>setRole(e.target.value)}>{Object.entries(roles).map(([value,title])=><option key={value} value={value}>{title}</option>)}</select></label><div className="actions"><button onClick={()=>{onSave(user,displayName,role);setEditing(null)}}>Зберегти</button><button className="secondary" onClick={()=>setEditing(null)}>Скасувати</button></div></div>:
        <div className="actions">{user.status!=='approved'&&<button onClick={()=>onStatus(user,'approved')}>Підтвердити</button>}{user.status!=='blocked'&&<button onClick={()=>onStatus(user,'blocked')}>Блокувати</button>}<button onClick={()=>{setEditing(id(user.id));setDisplayName(String(user.displayName));setRole(String(user.role))}}>Змінити ім’я та роль</button></div>}
    </div>)}
  </div>
}

export function AdminLogs({catalog}: {catalog:Catalog}){
  const when=(value:unknown)=>value?new Intl.DateTimeFormat('uk-UA',{dateStyle:'short',timeStyle:'short',timeZone:'Europe/Kyiv'}).format(new Date(String(value))):''
  return <div className="admin-logs">
    <details><summary>Історія відповідей ({catalog.templateVersions.length})</summary>{catalog.templateVersions.map((item,i)=><div className="record" key={i}><strong>{String(item.title)} · версія {String(item.version)}</strong><p>{String(item.text)}</p><small>{when(item.savedAtUtc)}</small></div>)}</details>
    <details><summary>Повідомлення для перевірки ({catalog.reviews.length})</summary>{catalog.reviews.map((item,i)=><div className="record" key={i}><strong>{name(catalog.chats,item.chatId)}</strong><p>{String(item.text)}</p><small>{String(item.parseError??'Потребує перевірки')}</small></div>)}</details>
    <details><summary>Доставка повідомлень ({catalog.outbox.length})</summary>{catalog.outbox.map((item,i)=><div className="record" key={i}><strong>{name(catalog.users,item.userId,'displayName')} · алгоритм №{String(item.incidentId)}</strong><p>{String(item.text)}</p><small>{deliveryStatuses[String(item.status)]||String(item.status)} · {when(item.sentAtUtc??item.dueAtUtc)}</small></div>)}</details>
    <details><summary>Журнал змін ({catalog.audit.length})</summary>{catalog.audit.map((item,i)=><div className="record" key={i}><strong>{item.actorUserId==null?'Система':name(catalog.users,item.actorUserId,'displayName')} · {auditActions[String(item.action)]||String(item.action)}</strong><small>{when(item.createdAtUtc)}</small></div>)}</details>
  </div>
}
