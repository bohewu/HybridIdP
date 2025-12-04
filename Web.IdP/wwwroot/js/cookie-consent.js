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

  function hideConsent(){
    var el = document.getElementById('cookie-consent-banner');
    if (el) el.style.display = 'none';
  }

  function accept(){
    setCookie('cookie_consent', 'accepted', 365);
    hideConsent();
  }

  function decline(){
    setCookie('cookie_consent', 'declined', 365);
    hideConsent();
  }

  document.addEventListener('DOMContentLoaded', function(){
    var consent = getCookie('cookie_consent');
    if (!consent){
      var container = document.createElement('div');
      container.innerHTML = document.getElementById('cookie-consent-template').innerHTML;
      document.body.appendChild(container);
      document.getElementById('cookie-consent-accept').addEventListener('click', accept);
      document.getElementById('cookie-consent-decline').addEventListener('click', decline);
      document.getElementById('cookie-consent-close').addEventListener('click', hideConsent);
    }
  });
})();
