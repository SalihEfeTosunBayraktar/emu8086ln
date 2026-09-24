; Mouse (INT 33h): shows the text cell under the mouse and the buttons. Press a key to stop.
; Move the mouse over the Screen panel.
include 'emu8086.inc'
#make_COM#
org 100h

    mov ax, 0               ; reset the mouse driver
    int 33h
    mov ax, 1               ; show the pointer
    int 33h
    CURSOROFF
track:
    mov ax, 3               ; CX = x, DX = y (pixels), BX = buttons
    int 33h
    GOTOXY 0, 0
    PRINT 'column '
    mov ax, cx
    shr ax, 3               ; 8 pixels per text column
    call PRINT_NUM_UNS
    PRINT '  row '
    mov ax, dx
    shr ax, 3
    call PRINT_NUM_UNS
    PRINT '  buttons '
    mov ax, bx
    call PRINT_NUM_UNS
    PRINT '   '
    mov ah, 01h
    int 16h
    jz track
    CURSORON
    ret

DEFINE_PRINT_NUM_UNS
END
