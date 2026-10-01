import { useState } from 'react'
import type { Dispatch, SetStateAction } from 'react'

export type Row = Record<string, unknown>
export type Catalog = { chats:Row[],objects:Row[],categories:Row[],algorithms:Row[],routes:Row[],templates:Row[],templateVersions:Row[],answers:Row[],incidentLabels:Row[],users:Row[],scopes:Row[],settings:{key:string,value:string}[],reviews:Row[],outbox:Row[],audit:Row[] }
export const specs:Record<string,[string,string][]>= {
  chats:[['telegramChatId','number'],['senderTelegramId','number'],['name','text'],['enabled','bool']],
  objects:[['code','text'],['name','text'],['enabled','bool']],
  algorithms:[['objectId','number'],['name','text'],['matchPattern','text'],['enabled','bool']],
  routes:[['userId','number']],
  templates:[['algorithmRuleId','number'],['title','text'],['text','text'],['enabled','bool']],
  scopes:[['userId','number']]
}
export const labels:Record<string,string>={chats:'Чати — джерела подій',objects:'Об’єкти',algorithms:'Типи алгоритмів',routes:'Відповідальні',templates:'Шаблони відповідей',scopes:'Права перегляду об’єктів'}
export const keyLabels:Record<string,string>={telegramChatId:'Telegram ID чату',senderTelegramId:'Telegram ID бота-відправника',code:'Код об’єкта',name:'Назва',enabled:'Увімкнено',objectId:'Об’єкт',categoryId:'Категорія',userId:'Працівник',chatId:'Чат',priority:'Пріоритет',matchPattern:'Правило розпізнавання',algorithmRuleId:'Тип алгоритму',title:'Назва відповіді',text:'Текст відповіді',sortOrder:'Порядок показу'}
const id=(x:unknown)=>String(x??'')
const find=(rows:Row[], value:unknown)=>rows.find(x=>id(x.id)===id(value))
const name=(rows:Row[], value:unknown, key='name')=>value==null?'':String(find(rows,value)?.[key]??'Невідомий запис')
const familyOptions=(catalog:Catalog)=>{
  const groups=new Map<string,Row>()
  for(const object of catalog.objects.filter(x=>x.enabled)){const family=String(object.code).replace(/\s+/g,'').replace(/\d+$/,'')||String(object.code);if(!groups.has(family))groups.set(family,object)}
  return [...groups].map(([family,object])=>({id:id(catalog.algorithms.find(x=>x.family===family&&x.enabled&&catalog.objects.some(o=>o.id===x.objectId&&o.enabled))?.objectId??object.id),label:family}))
}
const algorithmName=(catalog:Catalog,value:unknown)=>{const rule=find(catalog.algorithms,value);return rule?`${String(rule.name)} · ${String(rule.family)}`:'Невідомий алгоритм'}

export function AdminCatalog({catalog,formType,form,setForm,editId,onSubmit,onEdit,onCancel,onDelete}: {
  catalog:Catalog,formType:string,form:Record<string,string>,setForm:Dispatch<SetStateAction<Record<string,string>>>,editId:number|null,
  onSubmit:()=>void,onEdit:(row:Row)=>void,onCancel:()=>void,onDelete:(row:Row)=>void
}){
  const families=[...new Set(catalog.algorithms.filter(x=>x.enabled&&catalog.objects.some(o=>o.id===x.objectId&&o.enabled)).map(x=>String(x.family)))].sort((a,b)=>a.localeCompare(b,'uk'))
  const selectedFamily=form.family||String(find(catalog.algorithms,form.algorithmRuleId)?.family??'')
  const selectedUser=id(form.userId)
  const requiredObjects=new Set(catalog.routes.filter(route=>formType==='scopes'&&id(route.userId)===selectedUser&&route.enabled!==false&&route.objectId!=null).map(route=>id(route.objectId)))
  const selectedObjects=new Set([...((form.objectIds||'').split(',').filter(Boolean)),...requiredObjects])
  const toggleObject=(objectId:string)=>setForm(current=>{
    const ids=new Set((current.objectIds||'').split(',').filter(Boolean))
    if(formType==='scopes'&&requiredObjects.has(objectId))return current
    if(ids.has(objectId))ids.delete(objectId);else ids.add(objectId)
    return {...current,objectIds:[...ids].join(',')}
  })
  const groupedObjects=catalog.objects.filter(x=>x.enabled).reduce<Record<string,Row[]>>((groups,object)=>{
    const family=String(object.code).replace(/\s+/g,'').replace(/\d+$/,'')||String(object.code)
    ;(groups[family]??=[]).push(object)
    return groups
  },{})
  const relatedTemplates=catalog.templates.filter(x=>id(x.algorithmRuleId)===id(form.algorithmRuleId) && x.enabled)
  const responsibleUsers=[...new Set(catalog.routes.map(x=>id(x.userId)))].map(userId=>({userId,routes:catalog.routes.filter(x=>id(x.userId)===userId)}))
  const scopedUsers=[...new Set(catalog.scopes.map(x=>id(x.userId)))].map(userId=>({userId,scopes:catalog.scopes.filter(x=>id(x.userId)===userId)}))
  const options=(key:string):{id:string,label:string}[]|null=>{
    if(key==='objectId'&&formType==='algorithms')return familyOptions(catalog)
    if(key==='chatId')return catalog.chats.filter(x=>x.enabled).map(x=>({id:id(x.id),label:String(x.name)}))
    if(key==='objectId')return catalog.objects.filter(x=>x.enabled).map(x=>({id:id(x.id),label:`${String(x.name)} (${String(x.code)})`}))
    if(key==='categoryId')return catalog.categories.map(x=>({id:id(x.id),label:String(x.name)}))
    if(key==='userId')return catalog.users.filter(x=>!['routes','scopes'].includes(formType)||x.status==='approved').map(x=>({id:id(x.id),label:`${String(x.displayName)} · ${roles[String(x.role)]||String(x.role)}`}))
    if(key==='algorithmRuleId')return catalog.algorithms.filter(x=>x.enabled && String(x.family)===selectedFamily && catalog.objects.some(o=>o.id===x.objectId&&o.enabled)).map(x=>({id:id(x.id),label:String(x.name)}))
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
    if(formType==='scopes')return `${name(catalog.users,row.userId,'displayName')} · ${name(catalog.objects,row.objectId)}${find(catalog.objects,row.objectId)?.enabled===false?' (архівний об’єкт)':''}`
    if(formType==='algorithms')return `${String(row.name)} · ${String(row.family)}`
    if(formType==='templates')return `${String(row.title)} · ${algorithmName(catalog,row.algorithmRuleId)}`
    return String(row.name??row.code??'Запис')
  }
  const rows=(catalog[formType as keyof Catalog] as Row[]).filter(x=>x.enabled!==false)
  return <div className={`admin-workspace${formType==='templates'?' single':''}`}><div className="panel admin-editor"><h3>{formType==='routes'?'Призначити відповідального':formType==='scopes'?'Налаштувати права перегляду':formType==='templates'?'Відповіді на алгоритм':editId?'Редагувати запис':'Додати запис'}</h3>
    {formType==='routes'&&<p className="form-hint">Оберіть працівника та всі об’єкти його відповідальності. Чати лише приймають повідомлення і не впливають на розподіл. Збереження також відкриє працівнику доступ до вибраних об’єктів.</p>}
    {formType==='scopes'&&<p className="form-hint">Це право бачити алгоритми об’єкта й відповідати на них у Mini App. Сповіщення налаштовуються окремо у «Відповідальних»; при призначенні відповідального право перегляду додається автоматично. Тут можна надати доступ без сповіщень, наприклад керівнику.</p>}
    {formType==='chats'&&<p className="form-hint">Чат є лише джерелом повідомлень. Відповідальних призначають за об’єктами.</p>}
    {formType==='templates'&&<><p className="form-hint">Спочатку оберіть групу об’єктів, потім спільний для цієї групи алгоритм.</p><label>Група об’єктів<select value={selectedFamily} onChange={e=>setForm(x=>({...x,family:e.target.value,algorithmRuleId:'',title:'',text:''}))}><option value="">Оберіть групу</option>{families.map(family=><option key={family} value={family}>{family}</option>)}</select></label></>}
    {specs[formType].map(([key,type])=>{
      if(key==='matchPattern')return <details className="advanced-rule" key={key}><summary>Розширене правило розпізнавання</summary><p>Для звичайного алгоритму залиште порожнім: правило створиться з назви.</p><textarea value={form[key]??''} onChange={e=>setForm(x=>({...x,[key]:e.target.value}))} rows={2}/></details>
      const list=options(key)
      return <label key={key}>{label(key)}
        {list?<select value={form[key]??''} disabled={key==='objectId'&&formType==='algorithms'&&editId!==null || key==='algorithmRuleId'&&formType==='templates'&&editId!==null} onChange={e=>setForm(x=>key==='userId'&&formType==='routes'?{...x,userId:e.target.value,objectIds:catalog.routes.filter(route=>id(route.userId)===e.target.value && route.objectId!=null).map(route=>id(route.objectId)).join(',')}:key==='userId'&&formType==='scopes'?{...x,userId:e.target.value,objectIds:catalog.scopes.filter(scope=>id(scope.userId)===e.target.value).map(scope=>id(scope.objectId)).join(',')}:{...x,[key]:e.target.value})}>
          <option value="">{selectPlaceholder(key)}</option>{list.map(item=><option key={item.id} value={item.id}>{item.label}</option>)}
        </select>:type==='bool'?<select value={form[key]??'true'} onChange={e=>setForm(x=>({...x,[key]:e.target.value}))}><option value="true">Так</option><option value="false">Ні</option></select>:
          key==='text'||key==='matchPattern'?<textarea value={form[key]??''} onChange={e=>setForm(x=>({...x,[key]:e.target.value}))} rows={key==='text'?4:2}/>:
          <input type={type==='number'?'number':'text'} value={form[key]??''} onChange={e=>setForm(x=>({...x,[key]:e.target.value}))} placeholder={key==='senderTelegramId'?'0 — будь-який відправник':key==='priority'||key==='sortOrder'?'100 за замовчуванням':''}/>}
      </label>
    })}
    {['routes','scopes'].includes(formType)&&<><div className="picker-head"><strong>Об’єкти ({selectedObjects.size})</strong><button className="secondary" onClick={()=>setForm(x=>({...x,objectIds:catalog.objects.filter(o=>o.enabled).map(o=>id(o.id)).join(',')}))}>Вибрати всі</button><button className="secondary" onClick={()=>setForm(x=>({...x,objectIds:formType==='scopes'?[...requiredObjects].join(','):''}))}>Очистити</button></div><div className="object-picker">{Object.entries(groupedObjects).map(([family,objects])=><fieldset key={family}><legend>{family}</legend>{objects!.map(object=><label className="check-row" key={id(object.id)}><input type="checkbox" checked={selectedObjects.has(id(object.id))} disabled={requiredObjects.has(id(object.id))} onChange={()=>toggleObject(id(object.id))}/><span>{String(object.name)}{requiredObjects.has(id(object.id))?' · відповідальний':''}</span></label>)}</fieldset>)}</div></>}
    {formType==='templates'&&form.algorithmRuleId&&<div className="existing-answers"><h4>Наявні відповіді ({relatedTemplates.length})</h4>{relatedTemplates.length?relatedTemplates.map(template=><div className="answer-summary" key={id(template.id)}><b>{String(template.title)}</b><p>{String(template.text)}</p><div className="actions"><button className="secondary" onClick={()=>onEdit(template)}>Редагувати</button><button className="danger" onClick={()=>onDelete(template)}>Видалити</button></div></div>):<p>Для цього алгоритму відповідей ще немає.</p>}</div>}
    <div className="actions"><button onClick={onSubmit}>{formType==='routes'?'Зберегти призначення':formType==='scopes'?'Зберегти права':'Зберегти'}</button>{(editId||['routes','scopes'].includes(formType)&&selectedUser)&&<button className="secondary" onClick={onCancel}>Новий запис</button>}</div></div>
    {formType!=='templates'&&<div className="panel admin-records"><h3>{formType==='routes'?'Призначені працівники':formType==='scopes'?'Працівники з доступом':'Поточні записи'}</h3>{formType==='routes'?<div className="records">{responsibleUsers.length===0?<p className="empty">Призначень поки немає.</p>:responsibleUsers.map(({userId,routes})=><div className="record" key={userId}><strong>{name(catalog.users,userId,'displayName')}</strong><p>{routes.filter(x=>x.objectId!=null).map(x=>name(catalog.objects,x.objectId)).join(', ')||'Потрібно вибрати об’єкти: старе правило без об’єкта більше не діє.'}</p><div className="actions"><button onClick={()=>{setForm({userId,objectIds:routes.filter(x=>x.objectId!=null).map(x=>id(x.objectId)).join(',')})}}>Налаштувати</button><button className="danger" onClick={()=>onDelete({userId})}>Прибрати призначення</button></div></div>)}</div>:formType==='scopes'?<div className="records">{scopedUsers.length===0?<p className="empty">Прав перегляду поки немає.</p>:scopedUsers.map(({userId,scopes})=><div className="record" key={userId}><strong>{name(catalog.users,userId,'displayName')}</strong><p>{scopes.map(scope=>`${name(catalog.objects,scope.objectId)}${find(catalog.objects,scope.objectId)?.enabled===false?' (архівний)':''}`).join(', ')}</p><div className="actions"><button onClick={()=>setForm({userId,objectIds:scopes.map(scope=>id(scope.objectId)).join(',')})}>Налаштувати</button></div></div>)}</div>:<div className="records">{rows.length===0?<p className="empty">Поки немає записів.</p>:rows.map((row,i)=><div className="record" key={id(row.id??`${row.userId}-${row.objectId}-${i}`)}>
      <strong>{title(row)}</strong><div className="record-fields">{specs[formType].filter(([key])=>!['name','title','matchPattern'].includes(key)).map(([key])=><div key={key}><span>{label(key)}</span><b>{value(row,key)}</b></div>)}</div>
      <div className="actions"><button onClick={()=>onEdit(row)}>Редагувати</button><button className="danger" onClick={()=>onDelete(row)}>Видалити</button></div>
    </div>)}</div>}</div>}</div>
}

const roles:Record<string,string>={admin:'Адміністратор',operator:'Оператор',viewer:'Перегляд'}
const statuses:Record<string,string>={pending:'Очікує підтвердження',approved:'Підтверджено',denied:'Відхилено',blocked:'Заблоковано'}
const deliveryStatuses:Record<string,string>={pending:'Очікує надсилання',sent:'Надіслано',failed:'Помилка доставки',cancelled:'Скасовано'}
const auditActions:Record<string,string>={Added:'Додано',Modified:'Змінено',Deleted:'Видалено',respond:'Надано відповідь на',
  'import-telegram-export':'Імпортовано історію чату','import-history':'Імпортовано історичні дані',
  'consolidate-algorithms':'Об’єднано типи алгоритмів','remove-legacy-archive':'Прибрано старий архів'}
const auditEntities:Record<string,string>={AppUser:'користувача',UserScope:'право перегляду',SourceChat:'чат',
  MonitoredObject:'об’єкт',ProblemCategory:'категорію',AlgorithmRule:'тип алгоритму',RouteRule:'призначення відповідального',
  ResponseTemplate:'шаблон відповіді',TemplateVersion:'версію шаблону',Incident:'алгоритм',incident:'алгоритм',
  IncidentResponse:'відповідь',NotificationOutbox:'сповіщення',Setting:'налаштування',IncomingMessage:'повідомлення'}
const auditFields:Record<string,string>={Name:'Назва',Code:'Код',DisplayName:'Ім’я працівника',Username:'Telegram-нік',
  UserId:'Працівник',ObjectId:'Об’єкт',ChatId:'Чат',AlgorithmRuleId:'Алгоритм',Title:'Назва відповіді',Text:'Текст',
  Enabled:'Увімкнено',Status:'Стан доступу',Role:'Роль',TelegramChatId:'Telegram ID чату',
  SenderTelegramId:'Telegram ID відправника',TemplateId:'Шаблон',Version:'Версія',SortOrder:'Порядок показу',
  Key:'Налаштування',Value:'Значення',Eligible:'Повідомлень за 3 дні',imported:'Імпортовано',duplicates:'Пропущено повторних',review:'Потребують перевірки'}
const parseAuditDetail=(value:unknown):Record<string,string>=>Object.fromEntries(String(value??'').split(/,\s+(?=[A-Za-z][A-Za-z0-9]*=)/)
  .map(part=>{const separator=part.indexOf('=');return separator<0?null:[part.slice(0,separator),part.slice(separator+1)]})
  .filter((part):part is string[]=>part!==null))

export function AdminUsers({catalog,onSave,onStatus}: {
  catalog:Catalog,onSave:(user:Row,displayName:string,role:string)=>void,onStatus:(user:Row,status:string)=>void
}){
  const [editing,setEditing]=useState<string|null>(null)
  const [displayName,setDisplayName]=useState('')
  const [role,setRole]=useState('operator')
  return <div className="panel" id="access-requests"><h3>Користувачі та запити доступу{catalog.users.filter(user=>user.status==='pending').length>0&&` (${catalog.users.filter(user=>user.status==='pending').length} нових)`}</h3>{[...catalog.users].sort((a,b)=>Number(b.status==='pending')-Number(a.status==='pending')).map(user=>
    <div className="record" key={id(user.id)}><strong>{String(user.displayName)}</strong>
      <p>{roles[String(user.role)]||String(user.role)} · {statuses[String(user.status)]||String(user.status)}</p>
      {editing===id(user.id)?<div className="user-edit"><label>Службове ім’я<input value={displayName} onChange={e=>setDisplayName(e.target.value)}/></label><label>Роль<select value={role} onChange={e=>setRole(e.target.value)}>{Object.entries(roles).map(([value,title])=><option key={value} value={value}>{title}</option>)}</select></label><div className="actions"><button onClick={()=>{onSave(user,displayName,role);setEditing(null)}}>Зберегти</button><button className="secondary" onClick={()=>setEditing(null)}>Скасувати</button></div></div>:
        <div className="actions">{user.status!=='approved'&&<button onClick={()=>onStatus(user,'approved')}>Підтвердити</button>}{user.status!=='blocked'&&<button onClick={()=>onStatus(user,'blocked')}>Блокувати</button>}<button onClick={()=>{setEditing(id(user.id));setDisplayName(String(user.displayName));setRole(String(user.role))}}>Змінити ім’я та роль</button></div>}
    </div>)}
  </div>
}

export function AdminLogs({catalog}: {catalog:Catalog}){
  const when=(value:unknown)=>value?new Intl.DateTimeFormat('uk-UA',{dateStyle:'short',timeStyle:'short',timeZone:'Europe/Kyiv'}).format(new Date(String(value))):''
  const incidentLabel=(value:unknown)=>{
    const incident=find(catalog.incidentLabels,value)
    return incident?`${String(incident.algorithmName)} · ${String(incident.objectName)}`:'Алгоритм із журналу'
  }
  const auditTarget=(item:Row,fields:Record<string,string>)=>{
    const entity=String(item.entity)
    if(entity.toLowerCase()==='incident')return incidentLabel(item.entityId)
    if(entity==='RouteRule'||entity==='UserScope')return [name(catalog.users,fields.UserId,'displayName'),name(catalog.objects,fields.ObjectId)].filter(x=>x&&x!=='Невідомий запис').join(' · ')
    if(entity==='TemplateVersion'||entity==='ResponseTemplate')return fields.Title||name(catalog.templates,item.entityId,'title')
    if(entity==='AppUser')return fields.DisplayName||name(catalog.users,item.entityId,'displayName')
    if(entity==='SourceChat')return fields.Name||name(catalog.chats,item.entityId)
    if(entity==='MonitoredObject')return fields.Name||name(catalog.objects,item.entityId)
    if(entity==='AlgorithmRule')return fields.Name||name(catalog.algorithms,item.entityId)
    if(entity==='Setting')return fields.Key==='reminder_minutes'?'Інтервал нагадувань':fields.Key||''
    return fields.Name||fields.Title||''
  }
  const auditValue=(key:string,value:string)=>{
    if(key==='UserId')return name(catalog.users,value,'displayName')
    if(key==='ObjectId')return name(catalog.objects,value)
    if(key==='ChatId')return name(catalog.chats,value)
    if(key==='AlgorithmRuleId')return algorithmName(catalog,value)
    if(key==='TemplateId')return name(catalog.templates,value,'title')
    if(key==='Enabled')return value.toLowerCase()==='true'?'Так':'Ні'
    if(key==='Status')return statuses[value]||value
    if(key==='Role')return roles[value]||value
    if(key==='Key'&&value==='reminder_minutes')return 'Інтервал нагадувань'
    return value
  }
  return <div className="admin-logs">
    <details><summary>Надані відповіді на алгоритми ({catalog.answers.length} останніх)</summary>{catalog.answers.length===0?<p className="empty">Відповідей ще немає.</p>:catalog.answers.map(item=><div className="record history-item" key={id(item.id)}>
      <strong>{String(item.algorithmName)}</strong><p>{String(item.objectName)} · початок алгоритму {when(item.startedAtUtc)}</p>
      <p className="history-answer">{String(item.text)}</p><small>Відповів: <b>{String(item.authorName)}</b> · {when(item.createdAtUtc)}</small>
    </div>)}</details>
    <details><summary>Історія змін шаблонів ({catalog.templateVersions.length} останніх)</summary>{catalog.templateVersions.length===0?<p className="empty">Змін шаблонів ще немає.</p>:catalog.templateVersions.map(item=><div className="record history-item" key={id(item.id)}>
      <strong>{String(item.algorithmName)}</strong><p>{String(item.title)} · версія {String(item.version)}</p>
      <p className="history-answer">{String(item.text)}</p><small>Збережено: {when(item.savedAtUtc)}</small>
    </div>)}</details>
    <details><summary>Повідомлення для перевірки ({catalog.reviews.length})</summary>{catalog.reviews.map((item,i)=><div className="record" key={i}><strong>{name(catalog.chats,item.chatId)}</strong><p>{String(item.text)}</p><small>{String(item.parseError??'Потребує перевірки')}</small></div>)}</details>
    <details><summary>Доставка повідомлень ({catalog.outbox.length} останніх)</summary>{catalog.outbox.map((item,i)=><div className="record history-item" key={i}><strong>{incidentLabel(item.incidentId)}</strong><p>{String(item.text)}</p><small>Адресат: {name(catalog.users,item.userId,'displayName')} · {deliveryStatuses[String(item.status)]||String(item.status)} · {when(item.sentAtUtc??item.dueAtUtc)}</small></div>)}</details>
    <details><summary>Журнал змін ({catalog.audit.length} останніх)</summary>{catalog.audit.length===0?<p className="empty">Змін ще немає.</p>:catalog.audit.map(item=>{
      const fields=parseAuditDetail(item.detail)
      const entity=String(item.entity)
      const action=String(item.action)
      const standaloneAction=['import-telegram-export','import-history','consolidate-algorithms','remove-legacy-archive'].includes(action)
      const target=auditTarget(item,fields)
      const entries=Object.entries(fields).filter(([key])=>key in auditFields && !['Id','CreatedAtUtc'].includes(key))
      return <div className="record history-item" key={id(item.id)}>
        <strong>{item.actorUserId==null?'Система':name(catalog.users,item.actorUserId,'displayName')} · {auditActions[action]||'Оновлено'}{standaloneAction?'':` ${auditEntities[entity]||'запис'}`}{target&&` «${target}»`}</strong>
        <small>{when(item.createdAtUtc)}</small>
        {entries.length>0&&<p className="audit-fields">{entries.map(([key,value])=><span key={key}><b>{auditFields[key]}:</b> {auditValue(key,value)}</span>)}</p>}
      </div>
    })}</details>
  </div>
}
