#![cfg_attr(target_os="windows",windows_subsystem="windows")]
use serde_json::{json,Value};
use std::{io::{self,BufRead,Write},sync::Mutex};
use tauri::{Emitter,Manager,WebviewWindow};

#[derive(Default)]
struct Channel { ready: Vec<String>, prompt: Option<Value> }

fn write_backend(message:&Value) -> Result<(),String> {
    let mut out=io::stdout().lock();
    writeln!(out,"{}",message).and_then(|_|out.flush()).map_err(|e|e.to_string())
}

#[tauri::command]
fn dispatch(window:WebviewWindow,message:Value,state:tauri::State<Mutex<Channel>>) -> Result<(),String> {
    let ty=message["type"].as_str().ok_or("invalid message")?;
    let label=window.label();
    let mut ch=state.lock().map_err(|_|"desktop unavailable")?;
    if ty=="ready" {
        if !ch.ready.iter().any(|key|key==label) { ch.ready.push(label.to_owned()); }
        if ch.ready.len()==2 {
            if std::env::args().nth(1).as_deref()==Some("--fixture") {
                for name in ["manager","approval"] {
                    let w=window.app_handle().get_webview_window(name).ok_or("missing window")?;
                    w.emit("bridge",&json!({"type":"fixture"})).map_err(|e|e.to_string())?;
                    w.show().map_err(|e|e.to_string())?;
                }
            } else { write_backend(&json!({"type":"ready"}))?; }
        }
        return Ok(());
    }
    if std::env::args().nth(1).as_deref()==Some("--fixture") {
        if ty=="disconnect" { window.app_handle().exit(0); }
        else if label=="approval" && ty=="decide" { window.hide().map_err(|e|e.to_string())?; }
        return Ok(());
    }
    let allowed=match label {
        "approval"=>matches!(ty,"draft"|"decide"|"credentialReply") && ch.prompt.as_ref().is_some_and(|p|p["ticket"]==message["ticket"] && p["id"]==message["id"]),
        "manager"=>matches!(ty,"detail"|"reset"|"stopTask"|"newTask"|"disconnect"),
        _=>false
    };
    if !allowed { return Err("unauthorized window or stale prompt".into()); }
    write_backend(&message)
}

fn main() {
    tauri::Builder::default().manage(Mutex::new(Channel::default()))
      .invoke_handler(tauri::generate_handler![dispatch])
      .on_window_event(|win,event| {
          if let tauri::WindowEvent::CloseRequested{api,..}=event {
              if win.label()=="approval" {
                  api.prevent_close();
                  if let Ok(ch)=win.state::<Mutex<Channel>>().lock() {
                      if let Some(p)=&ch.prompt {
                          let reply=if p["type"]=="approval" {"decide"} else {"credentialReply"};
                          let _=write_backend(&json!({"type":reply,"id":p["id"],"ticket":p["ticket"],"choice":"reject","accepted":false}));
                      }
                  }
              } else { let _=write_backend(&json!({"type":"disconnect"}));win.app_handle().exit(0); }
          }
      })
      .setup(|app|{
          if std::env::args().nth(1).as_deref()==Some("--fixture") { return Ok(()); }
          let handle=app.handle().clone();
          std::thread::spawn(move||{
              let run=|| -> Result<(),Box<dyn std::error::Error>> {
                  let manager=handle.get_webview_window("manager").ok_or("manager missing")?;
                  let approval=handle.get_webview_window("approval").ok_or("approval missing")?;
                  for line in io::stdin().lock().lines(){
                      let m:Value=serde_json::from_str(&line?)?;
                      match m["type"].as_str() {
                          Some("approval"|"credential")=>{
                              handle.state::<Mutex<Channel>>().lock().unwrap().prompt=Some(m.clone());
                              approval.emit("bridge",&m)?;
                              approval.center()?; approval.show()?; approval.set_focus()?;
                          }
                          Some("dismiss")=>{
                              handle.state::<Mutex<Channel>>().lock().unwrap().prompt=None;
                              approval.hide()?;approval.emit("bridge",&m)?;
                          }
                          _=>{manager.emit("bridge",&m)?;if m["type"]=="init"{manager.show()?;}}
                      }
                  }
                  Ok(())
              };
              if run().is_err(){eprintln!("Desktop transport stopped.");}
              handle.exit(0);
          });
          Ok(())
      }).run(tauri::generate_context!()).expect("desktop startup failed");
}
