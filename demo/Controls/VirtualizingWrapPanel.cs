using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace ShowCuePlayer.Controls;

/// <summary>
/// A virtualizing wrap panel that arranges children in rows (left-to-right, top-to-bottom).
/// Supports UI virtualization for smooth scrolling through 10,000+ items.
/// Must be used inside an ItemsControl with VirtualizingPanel.IsVirtualizing=True.
/// </summary>
public class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
{
    #region Dependency Properties

    public static readonly DependencyProperty ItemWidthProperty =
        DependencyProperty.Register(nameof(ItemWidth), typeof(double), typeof(VirtualizingWrapPanel),
            new FrameworkPropertyMetadata(280.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ItemHeightProperty =
        DependencyProperty.Register(nameof(ItemHeight), typeof(double), typeof(VirtualizingWrapPanel),
            new FrameworkPropertyMetadata(320.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double ItemWidth
    {
        get => (double)GetValue(ItemWidthProperty);
        set => SetValue(ItemWidthProperty, value);
    }
    public double ItemHeight
    {
        get => (double)GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    #endregion

    #region IScrollInfo

    private ScrollViewer? _owner;
    private bool _canHorizontallyScroll;
    private bool _canVerticallyScroll;
    private Size _extent = Size.Empty;
    private Size _viewport = Size.Empty;
    private Point _offset;

    public ScrollViewer? ScrollOwner { get => _owner; set => _owner = value; }
    public bool CanHorizontallyScroll { get => _canHorizontallyScroll; set => _canHorizontallyScroll = value; }
    public bool CanVerticallyScroll { get => _canVerticallyScroll; set => _canVerticallyScroll = value; }
    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => _offset.X;
    public double VerticalOffset => _offset.Y;

    public void LineDown() => SetVerticalOffset(VerticalOffset + 48);
    public void LineUp() => SetVerticalOffset(VerticalOffset - 48);
    public void LineLeft() => SetHorizontalOffset(HorizontalOffset - 48);
    public void LineRight() => SetHorizontalOffset(HorizontalOffset + 48);
    public void PageDown() => SetVerticalOffset(VerticalOffset + ViewportHeight);
    public void PageUp() => SetVerticalOffset(VerticalOffset - ViewportHeight);
    public void PageLeft() => SetHorizontalOffset(HorizontalOffset - ViewportWidth);
    public void PageRight() => SetHorizontalOffset(HorizontalOffset + ViewportWidth);
    public void MouseWheelDown() => SetVerticalOffset(VerticalOffset + 80);
    public void MouseWheelUp() => SetVerticalOffset(VerticalOffset - 80);
    public void MouseWheelLeft() => SetHorizontalOffset(HorizontalOffset - 80);
    public void MouseWheelRight() => SetHorizontalOffset(HorizontalOffset + 80);

    public Rect MakeVisible(Visual visual, Rect rectangle) => rectangle;

    public void SetHorizontalOffset(double offset)
    {
        _offset.X = Math.Max(0, Math.Min(offset, ExtentWidth - ViewportWidth));
        _owner?.InvalidateScrollInfo();
        InvalidateMeasure();
    }

    public void SetVerticalOffset(double offset)
    {
        _offset.Y = Math.Max(0, Math.Min(offset, ExtentHeight - ViewportHeight));
        _owner?.InvalidateScrollInfo();
        InvalidateMeasure();
    }

    #endregion

    protected override Size MeasureOverride(Size availableSize)
    {
        double itemWidth = double.IsFinite(ItemWidth) && ItemWidth > 0 ? ItemWidth : 280;
        double itemHeight = double.IsFinite(ItemHeight) && ItemHeight > 0 ? ItemHeight : 320;
        double viewWidth = availableSize.Width;
        if (double.IsInfinity(viewWidth))
        {
            if (ScrollOwner != null)
                viewWidth = ScrollOwner.ViewportWidth > 0 ? ScrollOwner.ViewportWidth : ScrollOwner.ActualWidth;
            if (viewWidth <= 0 || double.IsInfinity(viewWidth))
                viewWidth = 1200;
        }

        double viewHeight = availableSize.Height;
        if (double.IsInfinity(viewHeight))
        {
            if (ScrollOwner != null)
                viewHeight = ScrollOwner.ViewportHeight > 0 ? ScrollOwner.ViewportHeight : ScrollOwner.ActualHeight;
            if (viewHeight <= 0 || double.IsInfinity(viewHeight))
                viewHeight = 800;
        }

        UpdateViewport(new Size(viewWidth, viewHeight));

        int itemsPerRow = Math.Max(1, (int)(viewWidth / itemWidth));
        var generator = ItemContainerGenerator as IItemContainerGenerator;
        var concreteGen = ItemContainerGenerator as ItemContainerGenerator;

        if (generator == null || concreteGen == null || ItemsControl.GetItemsOwner(this) is not ItemsControl itemsControl)
            return new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);

        int itemCount = itemsControl.Items.Count;
        int totalRows = (int)Math.Ceiling((double)itemCount / itemsPerRow);
        double totalHeight = totalRows * itemHeight;

        UpdateExtent(new Size(viewWidth, totalHeight));

        if (itemCount == 0) return new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, 0);

        // Calculate visible range
        int firstRow = Math.Max(0, (int)(_offset.Y / itemHeight));
        int lastRow = Math.Min(totalRows - 1, firstRow + (int)(viewHeight / itemHeight) + 1);
        int firstVisible = firstRow * itemsPerRow;
        int lastVisible = Math.Min(itemCount - 1, (lastRow + 1) * itemsPerRow - 1);

        // Recycle/remove out-of-view containers
        for (int i = InternalChildren.Count - 1; i >= 0; i--)
        {
            var child = InternalChildren[i];
            int itemIndex = concreteGen.IndexFromContainer(child);
            if (itemIndex < firstVisible || itemIndex > lastVisible || itemIndex < 0)
            {
                RemoveInternalChildRange(i, 1);
                if (itemIndex >= 0)
                {
                    var pos = generator.GeneratorPositionFromIndex(itemIndex);
                    if (pos.Index >= 0)
                        generator.Remove(pos, 1);
                }
            }
        }

        // Virtualize: generate visible items only
        var startPos = generator.GeneratorPositionFromIndex(firstVisible);
        int childIndex = startPos.Offset == 0 ? startPos.Index : startPos.Index + 1;

        using (generator.StartAt(startPos, GeneratorDirection.Forward, true))
        {
            for (int i = firstVisible; i <= lastVisible; i++)
            {
                bool isNew;
                if (generator.GenerateNext(out isNew) is not UIElement child)
                    break;
                if (isNew)
                {
                    if (childIndex >= InternalChildren.Count)
                    {
                        AddInternalChild(child);
                    }
                    else
                    {
                        InsertInternalChild(childIndex, child);
                    }
                    generator.PrepareItemContainer(child);
                }
                childIndex++;
                child.Measure(new Size(itemWidth, itemHeight));
            }
        }

        return new Size(viewWidth, Math.Min(viewHeight, totalHeight));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int itemsPerRow = Math.Max(1, (int)(finalSize.Width / ItemWidth));
        var generator = (ItemContainerGenerator)ItemContainerGenerator;

        if (ItemsControl.GetItemsOwner(this) is not ItemsControl itemsControl)
            return finalSize;

        foreach (UIElement child in InternalChildren)
        {
            int itemIndex = generator.IndexFromContainer(child);
            if (itemIndex < 0) continue;

            int row = itemIndex / itemsPerRow;
            int col = itemIndex % itemsPerRow;

            int rowStart = row * itemsPerRow;
            int rowCount = Math.Min(itemsPerRow, itemsControl.Items.Count - rowStart);
            double rowOffset = Math.Max(0, (finalSize.Width - rowCount * ItemWidth) / 2);
            double x = rowOffset + col * ItemWidth;
            double y = row * ItemHeight - _offset.Y;

            child.Arrange(new Rect(x, y, ItemWidth, ItemHeight));
        }

        return finalSize;
    }

    private void UpdateViewport(Size size)
    {
        if (_viewport == size) return;
        _viewport = size;
        _owner?.InvalidateScrollInfo();
    }

    private void UpdateExtent(Size size)
    {
        if (_extent == size) return;
        _extent = size;
        _owner?.InvalidateScrollInfo();
    }
}
