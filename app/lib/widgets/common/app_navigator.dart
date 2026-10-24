import 'package:flutter/widgets.dart';

/// Root navigator used by non-widget code (background download tasks) to show
/// a blocking security warning. Kept in its own file to avoid importing the
/// app shell from stores.
final GlobalKey<NavigatorState> appNavigatorKey = GlobalKey<NavigatorState>();
