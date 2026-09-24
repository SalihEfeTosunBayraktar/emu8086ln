; Boot sector: runs at 0000:7C00 like a PC booting from a floppy.
; 1) Run > Write program to virtual floppy (sector 1)  2) Run > Boot from virtual floppy.
; It also works with F5 directly.
#make_BOOT#
org 7C00h

    xor ax, ax
    mov ds, ax
    mov si, offset msg
next:
    lodsb                   ; AL = [DS:SI], SI++
    cmp al, 0
    je done
    mov ah, 0Eh             ; BIOS teletype (DOS is not available when booting)
    int 10h
    jmp next
done:
    hlt

msg db 'Hello from the boot sector!', 0
