// Local status feedback. No notifications, network, storage, or console payloads.
function show(message) {
  let region=document.getElementById('editor_status');
  if(!region){region=document.createElement('div');region.id='editor_status';region.setAttribute('role','status');document.body.appendChild(region);}
  region.textContent=String(message).slice(0,500);
  region.title=region.textContent;
  return {dismiss(){region.textContent='';}};
}
export default {error:show,success:show,warning:show,message:show};
