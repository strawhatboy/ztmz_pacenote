local resources = {};
local currentRow = nil;
local history = {};
local lastDispatchSignature = nil;
local transitionStartedAt = nil;
local transitionDuration = 0.28;
local whRatio = 3.0;
local historyFadeSlices = 12;
local historyFadeTopOpacity = 0.06;
local historyFadeBottomOpacity = 0.62;

function onInit(args)
    local gfx = args.Graphics;
    resources.font = gfx.CreateFont("Arial", 18, true, false);
    resources.currentText = gfx.CreateSolidBrush(255, 255, 255, 255);
    resources.unknownText = gfx.CreateSolidBrush(255, 214, 92, 255);
    resources.historyText = {};
    resources.historyUnknownText = {};
    resources.background = {};
    resetRoadbook();
end

function resetRoadbook()
    currentRow = nil;
    history = {};
    lastDispatchSignature = nil;
    transitionStartedAt = nil;
end

function getCollectionCount(collection)
    if collection == nil then
        return 0;
    end
    if collection.Count ~= nil then
        return collection.Count;
    end
    if collection.length ~= nil then
        return collection.length;
    end
    return 0;
end

function getCollectionItem(collection, index)
    return collection[index];
end

function appendToken(items, self, token)
    if token == nil then
        return;
    end
    local raw = tostring(token);
    if raw == "" then
        return;
    end
    local canonical = self.ResolvePacenoteIconToken(raw);
    table.insert(items, {
        token = raw,
        canonical = canonical,
        label = self.ResolvePacenoteDisplayName(raw),
        imageKey = canonical ~= "" and ("pacenote@" .. canonical) or nil
    });
end

function createRow(snapshot, self)
    local items = {};
    local tokens = snapshot.Tokens;
    local tokenCount = getCollectionCount(tokens);
    for tokenIndex = 0, tokenCount - 1 do
        local entry = getCollectionItem(tokens, tokenIndex);
        if entry ~= nil then
            appendToken(items, self, entry.Token);
            local modifiers = entry.Modifiers;
            local modifierCount = getCollectionCount(modifiers);
            for modifierIndex = 0, modifierCount - 1 do
                appendToken(items, self, getCollectionItem(modifiers, modifierIndex));
            end
        end
    end
    return { items = items };
end

function updateCurrentRow(args, historyRows)
    local snapshot = args.GameContext ~= nil and args.GameContext.CurrentPacenote or nil;
    if snapshot == nil or getCollectionCount(snapshot.Tokens) == 0 then
        if currentRow ~= nil or #history > 0 then
            resetRoadbook();
        end
        return;
    end

    local signature = tostring(snapshot.DispatchedAtUtc) .. ":" .. tostring(snapshot.Source);
    if signature == lastDispatchSignature then
        return;
    end

    if currentRow ~= nil and historyRows > 0 then
        table.insert(history, 1, currentRow);
    end
    while #history > historyRows do
        table.remove(history);
    end

    currentRow = createRow(snapshot, args.Self);
    lastDispatchSignature = signature;
    transitionStartedAt = args.GameData ~= nil and args.GameData.Time or nil;
end

function getTransitionProgress(args)
    if transitionStartedAt == nil or args.GameData == nil or args.GameData.Time == nil then
        return 1;
    end
    local elapsed = args.GameData.Time - transitionStartedAt;
    if elapsed < 0 then
        transitionStartedAt = args.GameData.Time;
        return 1;
    end
    local progress = math.max(0, math.min(1, elapsed / transitionDuration));
    return 1 - (1 - progress) * (1 - progress);
end

function getHistoryOpacityAtY(y, historyTop, historyBottom)
    if historyBottom <= historyTop then
        return historyFadeBottomOpacity;
    end
    local progress = math.max(0, math.min(1, (y - historyTop) / (historyBottom - historyTop)));
    return historyFadeTopOpacity + (historyFadeBottomOpacity - historyFadeTopOpacity) * progress;
end

function getHistoryTextBrush(gfx, opacity)
    local alpha = math.floor(math.max(0, math.min(1, opacity)) * 255 + 0.5);
    local brush = resources.historyText[alpha];
    if brush == nil then
        brush = gfx.CreateSolidBrush(235, 239, 241, alpha);
        resources.historyText[alpha] = brush;
    end
    return brush;
end

function getUnknownTextBrush(gfx, opacity)
    if opacity >= 1 then
        return resources.unknownText;
    end
    local alpha = math.floor(math.max(0, math.min(1, opacity)) * 255 + 0.5);
    local brush = resources.historyUnknownText[alpha];
    if brush == nil then
        brush = gfx.CreateSolidBrush(255, 214, 92, alpha);
        resources.historyUnknownText[alpha] = brush;
    end
    return brush;
end

function displayLabel(item)
    local label = item.label;
    if label == nil or label == "" then
        label = item.canonical ~= "" and item.canonical or item.token;
    end
    return string.gsub(label, "_", " ");
end

function drawCenteredText(gfx, font, fontSize, brush, centerX, top, text, maxWidth)
    local textSize = gfx.MeasureString(font, fontSize, text);
    if maxWidth ~= nil and maxWidth > 0 and textSize.X > maxWidth then
        fontSize = math.max(5, fontSize * maxWidth / textSize.X);
        textSize = gfx.MeasureString(font, fontSize, text);
    end
    gfx.DrawText(font, fontSize, brush, centerX - textSize.X * 0.5, top, text);
end

function drawRow(gfx, self, row, left, top, width, height, opacity, textBrush, showLabels, iconLabelGapRatio)
    if row == nil or row.items == nil or #row.items == 0 then
        return;
    end

    local count = #row.items;
    local horizontalPadding = math.max(8, height * 0.08);
    local availableWidth = math.max(1, width - horizontalPadding * 2);
    local labelHeight = showLabels and height * 0.22 or 0;
    local iconLabelGap = showLabels and height * iconLabelGapRatio or 0;
    local iconAreaHeight = math.max(1, height - labelHeight - iconLabelGap);
    local cellWidth = math.min(iconAreaHeight, availableWidth / count);
    local contentWidth = cellWidth * count;
    local startX = left + (width - contentWidth) * 0.5;
    local iconSize = math.min(iconAreaHeight * 0.88, cellWidth * 0.86);
    local iconTop = top + (iconAreaHeight - iconSize) * 0.5;

    for index, item in ipairs(row.items) do
        local centerX = startX + (index - 0.5) * cellWidth;
        local image = item.imageKey ~= nil and self.ImageResources[item.imageKey] or nil;
        if image ~= nil then
            gfx.DrawImage(
                image,
                centerX - iconSize * 0.5,
                iconTop,
                centerX + iconSize * 0.5,
                iconTop + iconSize,
                opacity);
        else
            local fallbackSize = math.max(10, math.min(22, iconSize * 0.20));
            drawCenteredText(
                gfx,
                resources.font,
                fallbackSize,
                getUnknownTextBrush(gfx, opacity),
                centerX,
                iconTop + iconSize * 0.38,
                displayLabel(item),
                cellWidth * 0.92);
        end

        if showLabels then
            local labelSize = math.max(9, math.min(18, labelHeight * 0.52));
            drawCenteredText(
                gfx,
                resources.font,
                labelSize,
                textBrush,
                centerX,
                top + iconAreaHeight + iconLabelGap + labelHeight * 0.18,
                displayLabel(item),
                cellWidth * 0.92);
        end
    end
end

function drawHistoryRow(
    gfx,
    self,
    row,
    left,
    rowTop,
    width,
    rowHeight,
    historyTop,
    historyBottom,
    transitionProgress,
    isNewest,
    showLabels,
    iconLabelGapRatio)
    for slice = 0, historyFadeSlices - 1 do
        local sliceTop = rowTop + rowHeight * slice / historyFadeSlices;
        local sliceBottom = rowTop + rowHeight * (slice + 1) / historyFadeSlices;
        local opacity = getHistoryOpacityAtY((sliceTop + sliceBottom) * 0.5, historyTop, historyBottom);
        if isNewest then
            opacity = 1 + (opacity - 1) * transitionProgress;
        end

        gfx.ClipRegionStart(left, sliceTop, left + width, sliceBottom);
        drawRow(
            gfx,
            self,
            row,
            left,
            rowTop,
            width,
            rowHeight,
            opacity,
            getHistoryTextBrush(gfx, opacity),
            showLabels,
            iconLabelGapRatio);
        gfx.ClipRegionEnd();
    end
end

function onUpdate(args)
    local self = args.Self;
    local historyRows = math.max(0, math.min(5, math.floor((self.GetConfigByKey("dashboards.pacenoteRoadbook.historyRows") or 3) + 0.5)));
    updateCurrentRow(args, historyRows);
    if currentRow == nil or currentRow.items == nil or #currentRow.items == 0 then
        return;
    end

    local gfx = args.Graphics;
    local showLabels = self.GetConfigByKey("dashboards.pacenoteRoadbook.showLabels") ~= false;
    local rowGapRatio = math.max(0, math.min(0.08, self.GetConfigByKey("dashboards.pacenoteRoadbook.rowGap") or 0.018));
    local iconLabelGapRatio = math.max(0, math.min(0.12, self.GetConfigByKey("dashboards.pacenoteRoadbook.iconLabelGap") or 0.02));
    local left, top, width, height = getDashboardPositionStart(self, gfx, whRatio);
    local visibleHistoryRows = math.min(historyRows, #history);
    local totalSlots = math.max(1, historyRows + 1);
    local rowGap = height * rowGapRatio;
    local rowHeight = (height - rowGap * historyRows) / totalSlots;
    local currentTop = top + historyRows * (rowHeight + rowGap);
    local progress = getTransitionProgress(args);
    local backgroundOpacity = math.max(0, math.min(1, self.GetConfigByKey("dashboards.settings.backgroundOpacity") or 0.42));
    local backgroundAlpha = math.floor(backgroundOpacity * 210 + 0.5);
    local backgroundBrush = resources.background[backgroundAlpha];
    if backgroundBrush == nil then
        backgroundBrush = gfx.CreateSolidBrush(8, 13, 16, backgroundAlpha);
        resources.background[backgroundAlpha] = backgroundBrush;
    end
    gfx.FillRoundedRectangle(backgroundBrush, left, top, left + width, top + height, math.max(5, height * 0.025));

    for slot = visibleHistoryRows, 1, -1 do
        local targetTop = currentTop - slot * (rowHeight + rowGap);
        local previousTop = targetTop + rowHeight + rowGap;
        local rowTop = previousTop + (targetTop - previousTop) * progress;
        drawHistoryRow(
            gfx,
            self,
            history[slot],
            left,
            rowTop,
            width,
            rowHeight,
            top,
            currentTop,
            progress,
            slot == 1,
            showLabels,
            iconLabelGapRatio);
    end

    drawRow(
        gfx,
        self,
        currentRow,
        left,
        currentTop,
        width,
        rowHeight,
        1,
        resources.currentText,
        showLabels,
        iconLabelGapRatio);
end

function onExit()
    resetRoadbook();
end
