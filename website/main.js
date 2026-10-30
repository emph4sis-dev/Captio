// Captio website: plays the hero's capture flow once when it comes into view.
// Progressive: without this script (or with reduced motion) the finished capture is shown.
(() => {
  const demo = document.querySelector('[data-demo]');
  if (!demo || !('IntersectionObserver' in window)) return;

  const reduced = window.matchMedia('(prefers-reduced-motion: reduce)');
  const replay = demo.querySelector('.demo-replay');
  // Delay before each stage: select, toolbar, annotate, redact, ready to copy.
  const stages = [250, 750, 450, 850, 650];
  let timers = [];
  let played = false;

  const clear = () => { timers.forEach(clearTimeout); timers = []; };

  // Jump to the finished state, used when the demo leaves the screen or the tab is hidden.
  const finish = () => {
    clear();
    demo.classList.remove('is-playing');
    demo.removeAttribute('data-stage');
    if (replay) replay.hidden = reduced.matches;
  };

  const play = () => {
    if (reduced.matches) return;
    clear();
    if (replay) replay.hidden = true;
    demo.classList.add('is-resetting', 'is-playing');
    demo.dataset.stage = '0';
    void demo.offsetWidth; // apply stage 0 without transitions
    demo.classList.remove('is-resetting');

    let at = 0;
    stages.forEach((delay, i) => {
      at += delay;
      timers.push(setTimeout(() => { demo.dataset.stage = String(i + 1); }, at));
    });
    timers.push(setTimeout(finish, at + 900));
  };

  const observer = new IntersectionObserver((entries) => {
    for (const entry of entries) {
      if (entry.isIntersecting && !played) {
        played = true;
        play();
      } else if (!entry.isIntersecting && demo.classList.contains('is-playing')) {
        finish();
      }
    }
  }, { threshold: 0.5 });
  observer.observe(demo);

  document.addEventListener('visibilitychange', () => {
    if (document.hidden && demo.classList.contains('is-playing')) finish();
  });

  if (replay) replay.addEventListener('click', play);
})();
