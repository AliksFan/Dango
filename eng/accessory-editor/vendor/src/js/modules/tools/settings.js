// Session-only settings. Native host owns persisted application settings.
export default class LocalSettings {
  get_setting(key) { return ({theme:'light',transparency:true,default_units:'pixels',default_units_short:'px',resolution:96,snap:false,guides:false,thick_guides:false,exit_confirm:false,enable_autoresize:false})[key]; }
}
