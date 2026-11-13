"""Project-generated component catalog. No C# parsing or guessed field schemas."""
import json
from pathlib import Path
from types import SimpleNamespace
import bpy

CATALOG={};ENUM_ITEMS={};COMPONENT_ITEMS=[]
COLLIDERS={'Vecxy.Physics.BoxCollider':'BOX','Vecxy.Physics.SphereCollider':'SPHERE','Vecxy.Physics.CapsuleCollider':'CAPSULE'}


def catalog_path(context):
    value=context.scene.vecxy_usd.catalog_path
    return Path(bpy.path.abspath(value)).resolve()


def reload_catalog(context,required=False):
    global CATALOG,COMPONENT_ITEMS
    path=catalog_path(context)
    if not path.exists():
        CATALOG={};COMPONENT_ITEMS=[]
        if required:raise RuntimeError('Build the game project first to generate '+str(path))
        return False
    document=json.loads(path.read_text(encoding='utf-8-sig'))
    if document.get('format')!=1:raise RuntimeError('Unsupported component catalog version')
    CATALOG={c['type']:c for c in document['components']}
    COMPONENT_ITEMS=[(c['type'],c['category']+' / '+c['label'],c.get('description','')) for c in sorted(CATALOG.values(),key=lambda c:(c['category'],c['label']))]
    for col in bpy.data.collections:upgrade_legacy(col.vecxy)
    for obj in bpy.data.objects:upgrade_legacy(obj.vecxy)
    return True


def component_items(self,context):
    return COMPONENT_ITEMS or [('NONE','Build/reload project components','No component catalog loaded')]


def enum_items(self,context):
    key=self.choices
    if key not in ENUM_ITEMS:ENUM_ITEMS[key]=[(v,v,'') for v in json.loads(key or '[]')]
    return ENUM_ITEMS[key]


def field_value(param):
    if param.kind=='VECTOR3':return {'x':param.vector[0],'y':param.vector[1],'z':param.vector[2]}
    return getattr(param,{'FLOAT':'number','INT':'integer','BOOL':'boolean','STRING':'text','ENUM':'enum_value'}[param.kind])


def set_param(component,field,value=None,override=True):
    param=next((p for p in component.parameters if p.key==field['key']),None)
    if param is None:param=component.parameters.add();param.key=field['key']
    param.kind=field['kind'];param.is_override=override
    value=field.get('default') if value is None else value
    if param.kind=='ENUM':param.choices=json.dumps(field.get('choices',[]));param.enum_value=str(value)
    elif param.kind=='VECTOR3':param.vector=tuple(value.values()) if isinstance(value,dict) else value
    else:setattr(param,{'FLOAT':'number','INT':'integer','BOOL':'boolean','STRING':'text'}[param.kind],value)
    return param


def values(component,include_defaults=True):
    definition=CATALOG.get(component.type_name)
    result={}
    if include_defaults and definition:
        for field in definition['fields']:
            value=field['default']
            result[field['key']]={'x':value[0],'y':value[1],'z':value[2]} if field['kind']=='VECTOR3' else value
    result.update({p.key:field_value(p) for p in component.parameters if p.is_override})
    if definition:
        for field in definition['fields']:
            value=result.get(field['key'])
            if value is None:continue
            entries=value.values() if isinstance(value,dict) else [value]
            if field['kind'] in ('FLOAT','INT','VECTOR3'):
                for number in entries:
                    if field.get('min') is not None and number<field['min'] or field.get('max') is not None and number>field['max']:
                        raise RuntimeError(definition['label']+': '+field['label']+' is outside the declared range')
    return result


def add_component(settings,type_name,override=False,key=None):
    definition=CATALOG.get(type_name)
    if not definition:raise RuntimeError('Component is not in the generated catalog: '+type_name)
    key=key or definition['key']
    component=next((c for c in settings.components if c.key==key),None)
    if component is None:component=settings.components.add()
    component.key=key;component.type_name=type_name;component.enabled=True
    component.parameters.clear()
    for field in definition['fields']:set_param(component,field,override=not override)
    return component


def upgrade_legacy(settings):
    if settings.collider=='NONE':return
    type_name=next((t for t,k in COLLIDERS.items() if k==settings.collider),None)
    if type_name not in CATALOG:return
    if not any(c.type_name in COLLIDERS for c in settings.components):
        component=add_component(settings,type_name,key='collider')
        data={'center':{'x':settings.collider_center[0],'y':settings.collider_center[1],'z':settings.collider_center[2]},
              'size':{'x':settings.collider_size[0],'y':settings.collider_size[1],'z':settings.collider_size[2]},
              'radius':settings.collider_radius,'height':settings.collider_height,'axis':'Z',
              'friction':settings.friction,'restitution':settings.restitution,'isTrigger':settings.trigger}
        for field in CATALOG[type_name]['fields']:
            if field['key'] in data:set_param(component,field,data[field['key']])
        if settings.override_collider:settings.override_components=True
    settings.collider='NONE';settings.override_collider=False


def collider_view(settings,base=None):
    components={c.key:c for c in base.components} if base else {}
    components.update({c.key:c for c in settings.components})
    for key,comp in components.items():
        if comp.type_name not in COLLIDERS or not comp.enabled:continue
        data={}
        if base:
            inherited=next((c for c in base.components if c.key==key),None)
            if inherited:data=values(inherited)
        data.update(values(comp,include_defaults=base is None or key not in {c.key for c in settings.components}))
        vector=lambda key,default:tuple(data.get(key,dict(zip('xyz',default))).values())
        return SimpleNamespace(collider=COLLIDERS[comp.type_name],collider_center=vector('center',(0,0,0)),
            collider_size=vector('size',(1,1,1)),collider_radius=data.get('radius',.35),
            collider_height=data.get('height',1.1),collider_axis=data.get('axis','Z'))
    return SimpleNamespace(collider='NONE')


def ensure_fields(component,overrides=False):
    definition=CATALOG.get(component.type_name)
    if not definition:return
    for field in definition['fields']:
        existing=next((p for p in component.parameters if p.key==field['key']),None)
        if existing:
            if not overrides:existing.is_override=True
            existing.kind=field['kind']
            if existing.kind=='ENUM':existing.choices=json.dumps(field.get('choices',[]))
        else:set_param(component,field,override=not overrides)
