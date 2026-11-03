/* ArchoeraCX web presence.
 *
 * The page is static; these are the only behaviours: the typed hero intro,
 * the site theme, copy, mobile navigation, the viewfinder header rule and a
 * short scroll reveal. Everything degrades to readable content without JS.
 */

(function () {
  "use strict";

  var reduced = window.matchMedia("(prefers-reduced-motion: reduce)");

  /* --------------------------------------------------------------- intro */

  // The first headline line is typed at the centre of the viewport, then flies
  // to its real position in the h1. The second line is typed after it lands.
  // The h1 itself is always in the DOM (the sr-only copy is the accessible
  // name); the intro only animates a visual duplicate.
  (function () {
    var root = document.documentElement;
    var intro = document.querySelector("[data-intro]");
    var hero = document.querySelector(".hero");
    var introText = document.querySelector("[data-intro-text]");
    var introCaret = document.querySelector("[data-intro-caret]");
    var line1 = document.querySelector('[data-hero-line="1"]');
    var line2 = document.querySelector('[data-hero-line="2"]');
    var line2Caret = document.querySelector("[data-hero-caret]");

    if (
      !root.classList.contains("intro-pending") ||
      reduced.matches ||
      !intro ||
      !hero ||
      !introText ||
      !introCaret ||
      !line1 ||
      !line2 ||
      !line2Caret
    ) {
      root.classList.remove("intro-pending");
      return;
    }

    // Deep links and restored scroll positions should not be hijacked by the
    // entrance; only a fresh visit at the top gets the full choreography.
    if (
      window.scrollY > 4 ||
      (window.location.hash && window.location.hash !== "#top")
    ) {
      root.classList.remove("intro-pending");
      return;
    }

    var introDone = false;
    var line2Done = false;
    var line2Timer = 0;
    var timers = [];
    var line2Chars = [];
    var readySent = false;
    var started = false;
    var skipAttached = false;

    function wait(fn, ms) {
      var id = window.setTimeout(fn, ms);
      timers.push(id);
      return id;
    }

    function clearTimers() {
      timers.forEach(function (id) {
        window.clearTimeout(id);
      });
      timers = [];
    }

    function easeOut(t) {
      return 1 - Math.pow(1 - t, 3);
    }

    function split(element, text, className) {
      element.textContent = "";
      var chars = [];
      Array.prototype.forEach.call(text, function (character) {
        var span = document.createElement("span");
        span.className = className;
        span.textContent = character;
        element.appendChild(span);
        chars.push(span);
      });
      return chars;
    }

    function prepareLine2() {
      if (line2Chars.length) return;
      line2Chars = split(line2, line2.textContent, "type-char");
    }

    function sendReady() {
      if (readySent) return;
      readySent = true;
      window.dispatchEvent(new CustomEvent("archoera:hero-ready"));
    }

    function removeSkip() {
      if (!skipAttached) return;
      skipAttached = false;
      document.removeEventListener("pointerdown", onSkip, true);
      document.removeEventListener("keydown", onSkip, true);
    }

    function finish(skip) {
      if (introDone) return;
      introDone = true;
      clearTimers();
      prepareLine2();
      root.classList.remove("intro-pending");
      hero.classList.add("is-ready");
      intro.classList.add("is-settling");
      intro.classList.add("is-done");
      window.setTimeout(function () {
        if (intro.parentNode) intro.parentNode.removeChild(intro);
      }, 220);
      sendReady();

      if (skip) {
        removeSkip();
        line2Done = true;
        line2Chars.forEach(function (char) {
          char.classList.add("is-on");
        });
        line2Caret.classList.remove("is-on");
      } else {
        line2Caret.classList.add("is-on");
        typeLine2();
      }
    }

    function typeLine2() {
      var index = 0;
      var step = function () {
        if (line2Done) return;
        if (index >= line2Chars.length) {
          line2Done = true;
          removeSkip();
          wait(function () {
            line2Caret.classList.remove("is-on");
          }, 900);
          return;
        }
        line2Chars[index].classList.add("is-on");
        index++;
        line2Timer = window.setTimeout(step, 62);
      };
      step();
    }

    function onSkip(event) {
      if (event.type === "keydown" && event.key !== "Escape") return;
      if (!introDone) {
        finish(true);
        return;
      }
      if (!line2Done) {
        line2Done = true;
        window.clearTimeout(line2Timer);
        removeSkip();
        line2Chars.forEach(function (char) {
          char.classList.add("is-on");
        });
        line2Caret.classList.remove("is-on");
      }
    }

    function attachSkip() {
      if (skipAttached) return;
      skipAttached = true;
      document.addEventListener("pointerdown", onSkip, true);
      document.addEventListener("keydown", onSkip, true);
    }

    function fly() {
      if (introDone) return;
      introCaret.style.visibility = "hidden";
      intro.classList.add("is-settling");
      var startRect = introText.getBoundingClientRect();
      var targetRect = line1.getBoundingClientRect();
      if (!startRect.width || !targetRect.width) {
        finish(true);
        return;
      }
      var scale = targetRect.width / startRect.width;
      var dx = targetRect.left - startRect.left;
      var dy = targetRect.top - startRect.top;
      introText.style.transformOrigin = "top left";
      introText.style.willChange = "transform";
      var start = window.performance.now();
      var duration = 820;
      var frame = function (now) {
        if (introDone) return;
        var t = Math.min(1, (now - start) / duration);
        var eased = easeOut(t);
        introText.style.transform =
          "translate(" +
          (dx * eased).toFixed(2) +
          "px," +
          (dy * eased).toFixed(2) +
          "px) scale(" +
          (1 + (scale - 1) * eased).toFixed(4) +
          ")";
        if (t < 1) {
          window.requestAnimationFrame(frame);
        } else {
          finish(false);
        }
      };
      window.requestAnimationFrame(frame);
    }

    function start() {
      if (started || introDone) return;
      if (!root.classList.contains("intro-pending")) return;
      started = true;
      attachSkip();
      var text = introText.getAttribute("data-text") || "";
      var introChars = split(introText, text, "type-char");
      var index = 0;
      var type = function () {
        if (introDone) return;
        if (index >= introChars.length) {
          wait(fly, 300);
          return;
        }
        introChars[index].classList.add("is-on");
        index++;
        wait(type, 78);
      };
      type();
    }

    if (document.fonts && document.fonts.ready) {
      document.fonts.ready.then(function () {
        window.requestAnimationFrame(start);
      });
      // Fonts are local, but never leave the caret waiting forever.
      wait(start, 600);
    } else {
      window.requestAnimationFrame(start);
    }
  })();

  /* --------------------------------------------------------------- theme */

  var rootElement = document.documentElement;
  var themeToggle = document.querySelector("[data-theme-toggle]");
  var themeMeta = document.querySelector('meta[name="theme-color"]');
  var themeScheme = document.querySelector('meta[name="color-scheme"]');
  var themeShots = Array.prototype.slice.call(
    document.querySelectorAll("[data-theme-shot]")
  );
  var themeCaption = document.querySelector("[data-theme-caption]");
  var heroMedia = document.querySelector(".hero__media");
  var scanTimer = null;

  var currentTheme = function () {
    return rootElement.getAttribute("data-theme") === "dark" ? "dark" : "light";
  };

  var scanHero = function () {
    if (!heroMedia || reduced.matches) return;
    heroMedia.classList.remove("is-scanning");
    // Restart the CSS animation from the top.
    void heroMedia.offsetWidth;
    heroMedia.classList.add("is-scanning");
    window.clearTimeout(scanTimer);
    scanTimer = window.setTimeout(function () {
      heroMedia.classList.remove("is-scanning");
    }, 900);
  };

  var syncTheme = function (theme) {
    var dark = theme === "dark";
    if (themeToggle) {
      themeToggle.setAttribute("aria-pressed", dark ? "true" : "false");
      themeToggle.setAttribute(
        "aria-label",
        dark ? "当前深色模式，切换到浅色模式" : "当前浅色模式，切换到深色模式"
      );
    }
    if (themeMeta) {
      themeMeta.setAttribute("content", dark ? "#10141B" : "#F7F8FB");
    }
    if (themeScheme) {
      themeScheme.setAttribute("content", dark ? "dark" : "light");
    }
    if (themeCaption) {
      themeCaption.textContent =
        theme === "dark" ? "主页 / 深色 · THEME" : "主页 / 浅色 · THEME";
    }
    themeShots.forEach(function (shot) {
      var active = shot.getAttribute("data-theme-shot") === theme;
      // The theme rules decide visibility; the class is only the no-JS fallback.
      shot.classList.remove("is-active");
      shot.setAttribute("aria-hidden", active ? "false" : "true");
    });
  };

  var persistTheme = function (theme) {
    try {
      window.localStorage.setItem("archoera.theme", theme);
    } catch (error) {
      // Private mode or a blocked storage API: the cookie below still helps.
    }
    // Mirror the choice in a first-party cookie so a refresh keeps it even
    // when localStorage is unavailable or cleared between visits.
    document.cookie =
      "archoera_theme=" + theme + "; path=/; max-age=31536000; SameSite=Lax";
  };

  var storedTheme = function () {
    try {
      var value = window.localStorage.getItem("archoera.theme");
      if (value === "light" || value === "dark") return value;
    } catch (error) {
      // Fall through to the cookie.
    }
    var match = document.cookie.match(/(?:^|; )archoera_theme=(light|dark)/);
    return match ? match[1] : null;
  };

  var commitTheme = function (theme) {
    rootElement.setAttribute("data-theme", theme);
    syncTheme(theme);
    persistTheme(theme);
  };

  var setTheme = function (theme, origin) {
    if (theme === currentTheme()) {
      // Clicking the already-active choice still makes it explicit, so a
      // refresh does not fall back to the OS preference.
      syncTheme(theme);
      persistTheme(theme);
      return;
    }

    if (
      typeof document.startViewTransition !== "function" ||
      reduced.matches ||
      !origin
    ) {
      commitTheme(theme);
      scanHero();
      return;
    }

    // Reveal the new theme as a circle from the control that changed it.
    var rect = origin.getBoundingClientRect();
    var x = rect.left + rect.width / 2;
    var y = rect.top + rect.height / 2;
    var radius = Math.hypot(
      Math.max(x, window.innerWidth - x),
      Math.max(y, window.innerHeight - y)
    );
    rootElement.style.setProperty("--theme-x", x + "px");
    rootElement.style.setProperty("--theme-y", y + "px");
    rootElement.style.setProperty("--theme-radius", radius + "px");

    var transition = document.startViewTransition(function () {
      commitTheme(theme);
    });

    var finish = function () {
      rootElement.style.removeProperty("--theme-x");
      rootElement.style.removeProperty("--theme-y");
      rootElement.style.removeProperty("--theme-radius");
      scanHero();
    };
    transition.finished.then(finish, finish);
  };

  if (themeToggle) {
    themeToggle.addEventListener("click", function () {
      setTheme(currentTheme() === "dark" ? "light" : "dark", themeToggle);
    });
  }

  // The inline head script resolved the theme before paint; mirror it into the
  // controls, then let the theme rules pick the matching screenshot.
  syncTheme(currentTheme());

  // With no explicit choice the site follows the OS for the whole visit, not
  // just at first paint.
  var systemTheme = window.matchMedia("(prefers-color-scheme: dark)");
  var followSystem = function (event) {
    if (storedTheme()) return;
    var theme = event.matches ? "dark" : "light";
    rootElement.setAttribute("data-theme", theme);
    syncTheme(theme);
  };
  if (systemTheme.addEventListener) {
    systemTheme.addEventListener("change", followSystem);
  } else if (systemTheme.addListener) {
    systemTheme.addListener(followSystem);
  }

  window.addEventListener(
    "archoera:hero-ready",
    function () {
      window.setTimeout(scanHero, 1400);
    },
    { once: true }
  );

  /* --------------------------------------------------------------- glow */

  var glowMedia = document.querySelector(".hero__media");
  var finePointer = window.matchMedia("(pointer: fine)");

  if (glowMedia && finePointer.matches && !reduced.matches) {
    var glowFrame = 0;
    var glowX = 0;
    var glowY = 0;

    var paintGlow = function () {
      glowFrame = 0;
      var rect = glowMedia.getBoundingClientRect();
      if (!rect.width || !rect.height) return;
      var x = ((glowX - rect.left) / rect.width) * 100;
      var y = ((glowY - rect.top) / rect.height) * 100;
      glowMedia.style.setProperty("--mx", x.toFixed(2) + "%");
      glowMedia.style.setProperty("--my", y.toFixed(2) + "%");
    };

    glowMedia.addEventListener("pointerenter", function () {
      glowMedia.classList.add("is-pointer");
    });

    glowMedia.addEventListener("pointermove", function (event) {
      glowX = event.clientX;
      glowY = event.clientY;
      if (glowFrame) return;
      glowFrame = window.requestAnimationFrame(paintGlow);
    });

    glowMedia.addEventListener("pointerleave", function () {
      glowMedia.classList.remove("is-pointer");
    });

    reduced.addEventListener("change", function (event) {
      if (event.matches) glowMedia.classList.remove("is-pointer");
    });
  }

  /* ---------------------------------------------------------------- copy */

  var copyButton = document.querySelector("[data-copy]");

  if (copyButton) {
    var copyStatus = document.querySelector("[data-copy-status]");
    var copySource = document.getElementById(
      copyButton.getAttribute("data-copy") || ""
    );
    var copyReset = null;
    var defaultNote = copyStatus ? copyStatus.textContent : "";

    var announce = function (message, label) {
      if (copyStatus) copyStatus.textContent = message;
      copyButton.textContent = label;
      window.clearTimeout(copyReset);
      copyReset = window.setTimeout(function () {
        copyButton.textContent = "复制";
        if (copyStatus) copyStatus.textContent = defaultNote;
      }, 2400);
    };

    var selectAndCopy = function () {
      if (!copySource) return;
      var range = document.createRange();
      var selection = window.getSelection();
      range.selectNodeContents(copySource);
      if (selection) {
        selection.removeAllRanges();
        selection.addRange(range);
      }
      try {
        var ok = document.execCommand("copy");
        announce(
          ok ? "命令已复制到剪贴板。" : "未能复制，请手动选择命令。",
          ok ? "已复制" : "复制失败"
        );
      } catch (error) {
        announce("未能复制，请手动选择命令。", "复制失败");
      }
      if (selection) selection.removeAllRanges();
    };

    copyButton.addEventListener("click", function () {
      if (!copySource) return;
      var text = copySource.innerText;

      if (navigator.clipboard && window.isSecureContext) {
        navigator.clipboard.writeText(text).then(
          function () {
            announce("命令已复制到剪贴板。", "已复制");
          },
          selectAndCopy
        );
        return;
      }

      selectAndCopy();
    });
  }

  /* --------------------------------------------------------------- header */

  var header = document.querySelector("[data-header]");
  var progressBar = document.querySelector("[data-progress]");
  var navLinks = Array.prototype.slice.call(
    document.querySelectorAll(".topnav a")
  );
  var sections = navLinks
    .map(function (link) {
      var href = link.getAttribute("href") || "";
      if (href.charAt(0) !== "#") return null;
      var section = document.getElementById(href.slice(1));
      return section ? { link: link, section: section } : null;
    })
    .filter(Boolean);

  var queued = false;

  var syncScroll = function () {
    queued = false;
    if (header) {
      header.classList.toggle("is-scrolled", window.scrollY > 8);
    }
    if (progressBar) {
      var max = document.documentElement.scrollHeight - window.innerHeight;
      var progress = max > 0 ? Math.min(1, Math.max(0, window.scrollY / max)) : 0;
      progressBar.style.transform = "scaleX(" + progress + ")";
    }

    var active = null;
    sections.forEach(function (item) {
      if (item.section.getBoundingClientRect().top <= 140) active = item;
    });
    sections.forEach(function (item) {
      if (item === active) {
        item.link.setAttribute("aria-current", "true");
      } else {
        item.link.removeAttribute("aria-current");
      }
    });
  };

  var onScroll = function () {
    if (queued) return;
    queued = true;
    window.requestAnimationFrame(syncScroll);
  };

  window.addEventListener("scroll", onScroll, { passive: true });
  window.addEventListener("resize", onScroll);
  syncScroll();

  /* ----------------------------------------------------------- mobile nav */

  var menuButton = document.querySelector("[data-menu-toggle]");
  var siteNav = document.getElementById("site-nav");

  var setMenu = function (open) {
    if (!menuButton || !siteNav) return;
    siteNav.classList.toggle("is-open", open);
    menuButton.setAttribute("aria-expanded", open ? "true" : "false");
    menuButton.setAttribute("aria-label", open ? "关闭导航" : "打开导航");
  };

  if (menuButton && siteNav) {
    menuButton.addEventListener("click", function () {
      setMenu(menuButton.getAttribute("aria-expanded") !== "true");
    });

    siteNav.addEventListener("click", function (event) {
      if (event.target.closest("a")) setMenu(false);
    });

    document.addEventListener("keydown", function (event) {
      if (event.key === "Escape") setMenu(false);
    });
  }

  /* --------------------------------------------------------------- reveal */

  if ("IntersectionObserver" in window && !reduced.matches) {
    var targets = document.querySelectorAll(
      ".section__rail, .section__body > h2, .section__lead, .ledger, .ui-facts, .contact, .diagram, .arch-notes, .build__grid, .platforms"
    );

    var observer = new IntersectionObserver(
      function (entries) {
        entries.forEach(function (entry) {
          if (!entry.isIntersecting) return;
          entry.target.classList.add("is-visible");
          observer.unobserve(entry.target);
        });
      },
      { rootMargin: "0px 0px -6% 0px", threshold: 0.06 }
    );

    Array.prototype.forEach.call(targets, function (target) {
      target.classList.add("reveal");
      observer.observe(target);
    });
  }
})();
