import 'package:flutter/widgets.dart';

import 'app_icon_data.dart';

/// Semantic icon vocabulary for the whole app.
///
/// Pages reference *meaning* (`AppIcons.download`, `AppIcons.modpack`) instead
/// of a concrete icon library, so the visual set can be swapped, extended or
/// re-cut without touching UI code. Every constant maps to a glyph in the
/// bundled `assets/fonts/AppIcons.ttf`.
///
/// Rules:
/// * Add new glyphs to `tool/icons/tabler-icons.txt` and run
///   `tool/build-icons.sh`; then expose them here with an intent-based name.
/// * Never reference `AppIconData` (or `Icons.*`) from a page or widget.
/// * Prefer reusing an existing name over adding a near-duplicate.
abstract final class AppIcons {
  // ---------------------------------------------------------------------
  // Navigation (command bar)
  // ---------------------------------------------------------------------
  static const IconData dashboard = AppIconData.layoutDashboard;
  static const IconData instances = AppIconData.layoutGrid;
  static const IconData download = AppIconData.download;
  static const IconData accounts = AppIconData.user;
  static const IconData wiki = AppIconData.book;
  static const IconData settings = AppIconData.adjustments;

  // ---------------------------------------------------------------------
  // Launch, tasks, lifecycle
  // ---------------------------------------------------------------------
  static const IconData play = AppIconData.playerPlay;
  static const IconData pause = AppIconData.playerPause;
  static const IconData running = AppIconData.loader;
  static const IconData completed = AppIconData.circleCheck;
  static const IconData cancelled = AppIconData.ban;
  static const IconData refresh = AppIconData.refresh;
  static const IconData reset = AppIconData.restore;

  // ---------------------------------------------------------------------
  // Generic actions
  // ---------------------------------------------------------------------
  static const IconData close = AppIconData.x;
  static const IconData add = AppIconData.plus;
  static const IconData remove = AppIconData.minus;
  static const IconData check = AppIconData.check;
  static const IconData search = AppIconData.search;
  static const IconData searchEmpty = AppIconData.searchOff;
  static const IconData filter = AppIconData.filter;
  static const IconData sort = AppIconData.arrowsSort;
  static const IconData more = AppIconData.dots;
  static const IconData chevronRight = AppIconData.chevronRight;
  static const IconData chevronDown = AppIconData.chevronDown;
  static const IconData back = AppIconData.arrowLeft;
  static const IconData externalLink = AppIconData.externalLink;
  static const IconData openExternal = AppIconData.arrowUpRight;
  static const IconData copy = AppIconData.copy;
  static const IconData delete = AppIconData.trash;
  static const IconData removeItem = AppIconData.circleMinus;
  static const IconData favorite = AppIconData.heart;

  // ---------------------------------------------------------------------
  // Status & feedback
  // ---------------------------------------------------------------------
  static const IconData error = AppIconData.alertCircle;
  static const IconData warning = AppIconData.alertTriangle;
  static const IconData info = AppIconData.infoCircle;

  // ---------------------------------------------------------------------
  // Content types
  // ---------------------------------------------------------------------
  static const IconData mod = AppIconData.puzzle;
  static const IconData modDisabled = AppIconData.puzzleOff;
  static const IconData modpack = AppIconData.packages;
  static const IconData resourcePack = AppIconData.photo;
  static const IconData imagePlaceholder = AppIconData.photoOff;
  static const IconData shader = AppIconData.sparkles;
  static const IconData news = AppIconData.article;
  static const IconData stable = AppIconData.certificate;
  static const IconData beta = AppIconData.flask;
  static const IconData modpackFile = AppIconData.fileText;

  // ---------------------------------------------------------------------
  // Instances, loaders, files
  // ---------------------------------------------------------------------
  static const IconData repair = AppIconData.tool;
  static const IconData folder = AppIconData.folder;
  static const IconData folderOpen = AppIconData.folderOpen;
  static const IconData fileDownload = AppIconData.fileDownload;
  static const IconData checklist = AppIconData.checklist;
  static const IconData list = AppIconData.list;
  static const IconData gridView = AppIconData.layoutGrid;
  static const IconData time = AppIconData.clock;

  // ---------------------------------------------------------------------
  // Downloads & network
  // ---------------------------------------------------------------------
  static const IconData source = AppIconData.server;
  static const IconData perHost = AppIconData.network;
  static const IconData proxy = AppIconData.route;
  static const IconData throughput = AppIconData.gauge;
  static const IconData offline = AppIconData.cloudOff;
  static const IconData cloudDownload = AppIconData.cloudDownload;
  static const IconData world = AppIconData.world;
  static const IconData java = AppIconData.coffee;
  static const IconData autoPick = AppIconData.sparkles;

  // ---------------------------------------------------------------------
  // Accounts & security
  // ---------------------------------------------------------------------
  static const IconData microsoft = AppIconData.brandWindows;
  static const IconData thirdPartyAuth = AppIconData.network;
  static const IconData security = AppIconData.shield;
  static const IconData privacy = AppIconData.shieldLock;
  static const IconData locked = AppIconData.lock;

  // ---------------------------------------------------------------------
  // Settings & about
  // ---------------------------------------------------------------------
  static const IconData appearance = AppIconData.palette;
  static const IconData colorPicker = AppIconData.colorPicker;
  static const IconData globalTint = AppIconData.contrast;
  static const IconData density = AppIconData.layoutRows;
  static const IconData reduceMotion = AppIconData.wind;
  static const IconData language = AppIconData.language;
  static const IconData storage = AppIconData.database;
  static const IconData memory = AppIconData.memory;
  static const IconData platform = AppIconData.devices;
  static const IconData core = AppIconData.settings;
  static const IconData version = AppIconData.tag;
  static const IconData repository = AppIconData.brandGithub;
  static const IconData issues = AppIconData.bug;
  static const IconData credits = AppIconData.heart;
  static const IconData license = AppIconData.gavel;
  static const IconData thirdParty = AppIconData.packages;
  static const IconData defaultItem = AppIconData.star;

  // ---------------------------------------------------------------------
  // Utilities
  // ---------------------------------------------------------------------
  static const IconData terminal = AppIconData.terminal2;
  static const IconData scan = AppIconData.listSearch;
  static const IconData quick = AppIconData.bolt;
  static const IconData window = AppIconData.appWindow;
  static const IconData notifications = AppIconData.bell;
}
