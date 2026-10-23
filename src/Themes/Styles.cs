namespace MarkdownEditor.Themes
{
    /// <summary>
    /// Kompakte, Theme-fähige Steuerelement-Stile (Menüs, Kontextmenü, Scrollbars, Buttons, Listen).
    /// Alle Farben kommen per DynamicResource aus <see cref="ThemeManager"/>.
    /// </summary>
    internal static class Styles
    {
        public const string Xaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>

  <!-- Menüleiste -->
  <Style TargetType='Menu'>
    <Setter Property='Background' Value='{DynamicResource ChromeBg}'/>
    <Setter Property='Foreground' Value='{DynamicResource ChromeFg}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Menu'>
          <Border Background='{TemplateBinding Background}' Padding='4,2'>
            <StackPanel IsItemsHost='True' Orientation='Horizontal'/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Kontextmenü -->
  <Style TargetType='ContextMenu'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Foreground' Value='{DynamicResource ChromeFg}'/>
    <Setter Property='Grid.IsSharedSizeScope' Value='True'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ContextMenu'>
          <Border Background='{DynamicResource MenuBg}' BorderBrush='{DynamicResource ChromeBorder}'
                  BorderThickness='1' Padding='3' CornerRadius='6'>
            <StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Cycle'/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='{x:Static MenuItem.SeparatorStyleKey}' TargetType='Separator'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Separator'>
          <Border Height='1' Margin='8,4' Background='{DynamicResource ChromeBorder}'/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Menüeintrag (Menüleiste, Untermenüs, Kontextmenü) -->
  <Style TargetType='MenuItem'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Foreground' Value='{DynamicResource ChromeFg}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='MenuItem'>
          <Grid SnapsToDevicePixels='True'>
            <Border x:Name='Bd' Background='Transparent' CornerRadius='4' Padding='4,5,8,5'>
              <Grid>
                <Grid.ColumnDefinitions>
                  <ColumnDefinition Width='Auto'/>
                  <ColumnDefinition Width='*'/>
                  <ColumnDefinition Width='Auto' SharedSizeGroup='MenuGesture'/>
                  <ColumnDefinition Width='Auto'/>
                </Grid.ColumnDefinitions>
                <TextBlock x:Name='Check' Width='22' Text='✓' FontWeight='Bold' TextAlignment='Center'
                           Foreground='{DynamicResource Accent}' VerticalAlignment='Center' Visibility='Hidden'/>
                <ContentPresenter Grid.Column='1' ContentSource='Header' RecognizesAccessKey='True' VerticalAlignment='Center'/>
                <TextBlock x:Name='Gesture' Grid.Column='2' Text='{TemplateBinding InputGestureText}'
                           Margin='28,0,0,0' Opacity='0.55' VerticalAlignment='Center'/>
                <TextBlock x:Name='Arrow' Grid.Column='3' Width='14' Text='›' TextAlignment='Right'
                           VerticalAlignment='Center' Visibility='Collapsed'/>
              </Grid>
            </Border>
            <Popup x:Name='PART_Popup' Placement='Right' HorizontalOffset='2' AllowsTransparency='True'
                   Focusable='False' PopupAnimation='Fade'
                   IsOpen='{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}}'>
              <Border Background='{DynamicResource MenuBg}' BorderBrush='{DynamicResource ChromeBorder}'
                      BorderThickness='1' Padding='3' CornerRadius='6'>
                <ScrollViewer VerticalScrollBarVisibility='Auto' MaxHeight='600'>
                  <StackPanel IsItemsHost='True' Grid.IsSharedSizeScope='True'
                              KeyboardNavigation.DirectionalNavigation='Cycle'/>
                </ScrollViewer>
              </Border>
            </Popup>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='Role' Value='TopLevelHeader'>
              <Setter TargetName='Check' Property='Visibility' Value='Collapsed'/>
              <Setter TargetName='Gesture' Property='Visibility' Value='Collapsed'/>
              <Setter TargetName='PART_Popup' Property='Placement' Value='Bottom'/>
              <Setter TargetName='Bd' Property='Padding' Value='10,4'/>
            </Trigger>
            <Trigger Property='Role' Value='TopLevelItem'>
              <Setter TargetName='Check' Property='Visibility' Value='Collapsed'/>
              <Setter TargetName='Gesture' Property='Visibility' Value='Collapsed'/>
              <Setter TargetName='Bd' Property='Padding' Value='10,4'/>
            </Trigger>
            <Trigger Property='Role' Value='SubmenuHeader'>
              <Setter TargetName='Arrow' Property='Visibility' Value='Visible'/>
            </Trigger>
            <Trigger Property='IsHighlighted' Value='True'>
              <Setter TargetName='Bd' Property='Background' Value='{DynamicResource MenuHover}'/>
            </Trigger>
            <Trigger Property='IsSubmenuOpen' Value='True'>
              <Setter TargetName='Bd' Property='Background' Value='{DynamicResource MenuHover}'/>
            </Trigger>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='Check' Property='Visibility' Value='Visible'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Opacity' Value='0.45'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='ToolTip'>
    <Setter Property='Background' Value='{DynamicResource MenuBg}'/>
    <Setter Property='Foreground' Value='{DynamicResource ChromeFg}'/>
    <Setter Property='BorderBrush' Value='{DynamicResource ChromeBorder}'/>
  </Style>

  <!-- Buttons -->
  <Style TargetType='Button'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Foreground' Value='{DynamicResource ChromeFg}'/>
    <Setter Property='Background' Value='{DynamicResource MenuBg}'/>
    <Setter Property='BorderBrush' Value='{DynamicResource ChromeBorder}'/>
    <Setter Property='Padding' Value='12,5'/>
    <Setter Property='Margin' Value='0,0,6,0'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='Bd' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
                  BorderThickness='1' CornerRadius='4' Padding='{TemplateBinding Padding}'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' RecognizesAccessKey='True'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='Bd' Property='Background' Value='{DynamicResource MenuHover}'/>
            </Trigger>
            <Trigger Property='IsDefault' Value='True'>
              <Setter TargetName='Bd' Property='BorderBrush' Value='{DynamicResource Accent}'/>
            </Trigger>
            <Trigger Property='IsPressed' Value='True'>
              <Setter TargetName='Bd' Property='BorderBrush' Value='{DynamicResource Accent}'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Opacity' Value='0.45'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='TextBox'>
    <Setter Property='Background' Value='{DynamicResource EditorBg}'/>
    <Setter Property='Foreground' Value='{DynamicResource ChromeFg}'/>
    <Setter Property='CaretBrush' Value='{DynamicResource ChromeFg}'/>
    <Setter Property='BorderBrush' Value='{DynamicResource ChromeBorder}'/>
    <Setter Property='Padding' Value='4,3'/>
  </Style>

  <!-- Seitenleiste -->
  <Style TargetType='ListBox'>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Foreground' Value='{DynamicResource ChromeFg}'/>
    <Setter Property='BorderThickness' Value='0'/>
    <Setter Property='ScrollViewer.HorizontalScrollBarVisibility' Value='Disabled'/>
  </Style>

  <Style TargetType='ListBoxItem'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Padding' Value='8,4'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ListBoxItem'>
          <Border x:Name='Bd' Background='Transparent' CornerRadius='4' Margin='4,1' Padding='{TemplateBinding Padding}'>
            <ContentPresenter/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='Bd' Property='Background' Value='{DynamicResource MenuHover}'/>
            </Trigger>
            <Trigger Property='IsSelected' Value='True'>
              <Setter TargetName='Bd' Property='Background' Value='{DynamicResource MenuHover}'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Tabellen-Dialog -->
  <Style TargetType='DataGridColumnHeader'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Foreground' Value='{DynamicResource ChromeFg}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='DataGridColumnHeader'>
          <Border Background='{DynamicResource ChromeBg}' BorderBrush='{DynamicResource ChromeBorder}'
                  BorderThickness='0,0,1,1' Padding='8,5'>
            <ContentPresenter/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Schlanke Scrollbars -->
  <Style x:Key='SbThumb' TargetType='Thumb'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Thumb'>
          <Border Background='{DynamicResource ScrollThumb}' CornerRadius='4' Margin='2'/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='SbPage' TargetType='RepeatButton'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='IsTabStop' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='RepeatButton'>
          <Border Background='Transparent'/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='ScrollBar'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Width' Value='12'/>
    <Setter Property='MinWidth' Value='12'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ScrollBar'>
          <Grid Background='Transparent'>
            <Track x:Name='PART_Track' IsDirectionReversed='True'>
              <Track.DecreaseRepeatButton>
                <RepeatButton Style='{StaticResource SbPage}' Command='ScrollBar.PageUpCommand'/>
              </Track.DecreaseRepeatButton>
              <Track.Thumb>
                <Thumb Style='{StaticResource SbThumb}'/>
              </Track.Thumb>
              <Track.IncreaseRepeatButton>
                <RepeatButton Style='{StaticResource SbPage}' Command='ScrollBar.PageDownCommand'/>
              </Track.IncreaseRepeatButton>
            </Track>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
    <Style.Triggers>
      <Trigger Property='Orientation' Value='Horizontal'>
        <Setter Property='Width' Value='Auto'/>
        <Setter Property='MinWidth' Value='0'/>
        <Setter Property='Height' Value='12'/>
        <Setter Property='MinHeight' Value='12'/>
        <Setter Property='Template'>
          <Setter.Value>
            <ControlTemplate TargetType='ScrollBar'>
              <Grid Background='Transparent'>
                <Track x:Name='PART_Track'>
                  <Track.DecreaseRepeatButton>
                    <RepeatButton Style='{StaticResource SbPage}' Command='ScrollBar.PageLeftCommand'/>
                  </Track.DecreaseRepeatButton>
                  <Track.Thumb>
                    <Thumb Style='{StaticResource SbThumb}'/>
                  </Track.Thumb>
                  <Track.IncreaseRepeatButton>
                    <RepeatButton Style='{StaticResource SbPage}' Command='ScrollBar.PageRightCommand'/>
                  </Track.IncreaseRepeatButton>
                </Track>
              </Grid>
            </ControlTemplate>
          </Setter.Value>
        </Setter>
      </Trigger>
    </Style.Triggers>
  </Style>
</ResourceDictionary>";
    }
}
