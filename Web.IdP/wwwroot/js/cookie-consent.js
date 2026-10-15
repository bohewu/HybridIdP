(function(){
  function setCookie(name, value, days){
    var expires = "";
    if (days){
      var date = new Date();
      date.setTime(date.getTime() + (days*24*60*60*1000));
      expires = "; expires=" + date.toUTCString();
    }
    document.cookie = name + "=" + (value || "") + expires + "; path=/";
  }
  function getCookie(name){
    var nameEQ = name + "=";
    var ca = document.cookie.split(';');
    for(var i=0;i < ca.length;i++){
      var c = ca[i];
      while (c.charAt(0)==' ') c = c.substring(1,c.length);
      if (c.indexOf(nameEQ) == 0) return c.substring(nameEQ.length,c.length);
    }
    return null;
  }

  function hideNotice(){
    var el = document.getElementById('cookie-consent-banner');
    if (el) el.style.display = 'none';
  }

  function acknowledge(){
    setCookie('cookie_notice', 'acknowledged', 365);
    document.cookie = 'cookie_consent=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/';
    hideNotice();
  }

  function showBanner(){
    var acknowledged = getCookie('cookie_notice') === 'acknowledged';
    if (!acknowledged){
      var container = document.createElement('div');
      container.innerHTML = document.getElementById('cookie-consent-template').innerHTML;
      // Prepend so it appears quickly (helpful on login page)
      if (document.body.firstChild) document.body.insertBefore(container, document.body.firstChild);
      else document.body.appendChild(container);
      var button = document.getElementById('cookie-notice-acknowledge');
      if (button) button.addEventListener('click', acknowledge);
    }
  }

  document.addEventListener('DOMContentLoaded', function(){
    showBanner();
  });

  // If script loaded after DOMContentLoaded, try to show immediately
  if (document.readyState === 'interactive' || document.readyState === 'complete'){
    try { showBanner(); } catch(e){}
  }
})();
